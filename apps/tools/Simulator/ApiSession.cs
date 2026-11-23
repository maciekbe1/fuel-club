using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FuelClub.Simulator;

/// <summary>
/// One signed-in identity against the public API, as the browser does it: cookies, a CSRF token
/// for each change and an Idempotency-Key where the API takes one. A 429 waits for Retry-After.
/// </summary>
public sealed class ApiSession : IDisposable
{
    private const string CsrfHeader = "X-CSRF-TOKEN";
    private const int MaxRateLimitRetries = 5;
    private readonly HttpClient _http;

    public ApiSession(string apiUrl)
    {
        _http = new HttpClient(
            new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true }
        )
        {
            BaseAddress = new Uri(apiUrl),
            Timeout = TimeSpan.FromSeconds(60),
        };
    }

    public Task<HttpResponseMessage> GetAsync(string url, CancellationToken ct) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), ct);

    public async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        using var response = await GetAsync(url, ct);
        await EnsureAsync(response, url, ct);
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct))!;
    }

    public Task<HttpResponseMessage> PostAsync(
        string url,
        object? body,
        CancellationToken ct,
        string? idempotencyKey = null,
        HttpMethod? method = null
    ) =>
        SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(method ?? HttpMethod.Post, url)
                {
                    Content = body is null ? null : JsonContent.Create(body),
                };
                if (idempotencyKey is not null)
                    request.Headers.Add("Idempotency-Key", idempotencyKey);
                return request;
            },
            ct,
            csrf: true
        );

    private async Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> build,
        CancellationToken ct,
        bool csrf = false
    )
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = build();
            if (csrf)
                request.Headers.Add(CsrfHeader, await CsrfAsync(ct));
            var response = await _http.SendAsync(request, ct);
            if (
                response.StatusCode != HttpStatusCode.TooManyRequests
                || attempt >= MaxRateLimitRetries
            )
                return response;
            var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
            response.Dispose();
            Console.WriteLine($"  429 on {request.RequestUri}: waiting {wait.TotalSeconds:F0} s");
            await Task.Delay(wait + TimeSpan.FromMilliseconds(Random.Shared.Next(100, 600)), ct);
        }
    }

    private async Task<string> CsrfAsync(CancellationToken ct)
    {
        var response = await _http.GetFromJsonAsync<JsonElement>("/api/auth/csrf", ct);
        return response.GetProperty("token").GetString()!;
    }

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Fails with the status and the problem code only - never the body of a secret.</summary>
    public static async Task EnsureAsync(
        HttpResponseMessage response,
        string what,
        CancellationToken ct
    )
    {
        if (response.IsSuccessStatusCode)
            return;
        throw new ApiException(what, response.StatusCode, await ProblemCodeAsync(response, ct));
    }

    public static async Task<string?> ProblemCodeAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions, ct);
            return problem.TryGetProperty("code", out var code) ? code.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose() => _http.Dispose();
}

public sealed class ApiException(string what, HttpStatusCode status, string? code)
    : Exception($"{what}: {(int)status} {code}")
{
    public HttpStatusCode Status { get; } = status;
    public string? Code { get; } = code;
}

/// <summary>
/// Client-side pace below an API limit (per minute). The server limit still decides; this only
/// keeps the simulator from hitting it on purpose.
/// </summary>
public sealed class Pacer(int perMinute)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(1) / Math.Max(1, perMinute);
    private DateTimeOffset _next = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var now = DateTimeOffset.UtcNow;
            if (_next > now)
                await Task.Delay(_next - now, ct);
            _next = (_next > now ? _next : now) + _interval;
        }
        finally
        {
            _gate.Release();
        }
    }
}

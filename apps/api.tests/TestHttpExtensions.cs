using System.Net;
using System.Net.Http.Json;

namespace FuelClub.Api.Tests;

public static class TestHttpExtensions
{
    public const string CsrfHeader = "X-CSRF-TOKEN";

    private sealed record CsrfResponse(string Token);

    public static async Task<string> GetCsrfTokenAsync(this HttpClient client) =>
        (await client.GetFromJsonAsync<CsrfResponse>("/api/auth/csrf"))!.Token;

    public static async Task<HttpResponseMessage> PostWithCsrfAsync(this HttpClient client, string url, object? body)
    {
        var token = await client.GetCsrfTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add(CsrfHeader, token);
        return await client.SendAsync(request);
    }

    public static async Task LoginAsync(this HttpClient client, string email, string password)
    {
        var response = await client.PostWithCsrfAsync("/api/auth/login", new { email, password });
        if (response.StatusCode != HttpStatusCode.NoContent)
            throw new InvalidOperationException($"Login failed for test account: {(int)response.StatusCode}");
    }
}

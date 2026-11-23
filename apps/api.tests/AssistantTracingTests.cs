using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant.Model;
using FuelClub.Api.Modules.Assistant.Tools;
using FuelClub.Api.Modules.Assistant.Tracing;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class AssistantTracingTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Secretish = "moje-tajne-pytanie-7f3a";
    private readonly List<string> _emails = [];
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
        await DbAsync(db =>
            db.RewardOffers.Where(offer => offer.Name.Contains(_tag)).ExecuteDeleteAsync()
        );
    }

    private sealed record Customer(string Email, string UserId, HttpClient Client);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<Customer> AccountAsync(WebApplicationFactory<Program>? host = null)
    {
        var email = $"assistant-trace-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, RoleNames.User);
        var userId = await DbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()
        );
        await DbAsync(async db =>
        {
            db.LoyaltyAccounts.Add(new LoyaltyAccount { UserId = userId, Balance = 1000 });
            return await db.SaveChangesAsync();
        });
        var client = (host ?? factory).CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        return new Customer(email, userId, client);
    }

    /// <summary>An active coupon: its code must never reach a trace.</summary>
    private async Task<string> ActiveCouponCodeAsync(Customer customer)
    {
        var offer = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = $"Slad {_tag}",
            Description = "",
            PointsCost = 1,
            DiscountPerLiter = 0.10m,
            MaxLiters = 10m,
            IsActive = true,
        };
        await DbAsync(async db =>
        {
            db.RewardOffers.Add(offer);
            return await db.SaveChangesAsync();
        });
        var exchange = await SendAsync(
            customer.Client,
            $"/api/rewards/{offer.Id}/exchange",
            new { version = 1 }
        );
        var id = (await exchange.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("coupon")
            .GetProperty("id")
            .GetGuid();
        var active = await SendAsync(customer.Client, $"/api/me/coupons/{id}/activate", null);
        return (await active.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("code")
            .GetString()!;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        string url,
        object? body
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add(Idempotency.HeaderName, Guid.NewGuid().ToString());
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static async Task<(string CorrelationId, string Raw)> ChatAsync(
        HttpClient client,
        string message
    )
    {
        var response = await client.PostWithCsrfAsync(
            "/api/chat",
            new { message, conversationId = await client.StartConversationAsync() }
        );
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        return (response.Headers.GetValues("X-Correlation-Id").Single(), raw);
    }

    private Task<AssistantTurn> TraceAsync(string correlationId) =>
        DbAsync(db =>
            db.AssistantTurns.AsNoTracking().SingleAsync(t => t.CorrelationId == correlationId)
        );

    private sealed class ScriptedModel(Func<int, CancellationToken, Task<ModelStep>> next)
        : IChatModel
    {
        private int _step;
        public string Provider => "test";
        public string Model => "scripted";
        public string PromptVersion => "trace-v1";
        public bool Demo => false;

        public Task<ModelStep> NextAsync(ChatModelContext context, CancellationToken ct) =>
            next(_step++, ct);
    }

    private WebApplicationFactory<Program> Host(
        IChatModel? model = null,
        string? otlpEndpoint = null,
        string? pseudonymKey = null
    ) =>
        factory.WithWebHostBuilder(builder =>
        {
            // UseSetting: read while services are registered, before the app is built.
            if (otlpEndpoint is not null)
                builder.UseSetting(Telemetry.EndpointKey, otlpEndpoint);
            if (pseudonymKey is not null)
                builder.UseSetting("Assistant:PseudonymKey", pseudonymKey);
            if (model is not null)
                builder.ConfigureTestServices(services => services.AddSingleton(model));
        });

    // ---- spans ----

    [Fact]
    public async Task Spans_carry_only_listed_attributes_and_no_content_codes_or_identity()
    {
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AssistantTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);
        var customer = await AccountAsync();
        var code = await ActiveCouponCodeAsync(customer);

        var (correlationId, raw) = await ChatAsync(
            customer.Client,
            $"Pokaż moje kupony i saldo {Secretish}"
        );

        var turn = Assert.Single(
            spans,
            s =>
                s.OperationName == AssistantTelemetry.TurnSpan
                && (string?)s.GetTagItem(AssistantTelemetry.Attributes.CorrelationId)
                    == correlationId
        );
        var tools = spans.Where(s => s.ParentSpanId == turn.SpanId).ToList();
        Assert.Equal(2, tools.Count);
        Assert.All(tools, t => Assert.Equal(AssistantTelemetry.ToolSpan, t.OperationName));
        Assert.Equal(
            ["get_my_balance", "list_my_coupons"],
            tools.Select(t => (string)t.GetTagItem(AssistantTelemetry.Attributes.ToolName)!).Order()
        );
        Assert.Equal("done", turn.GetTagItem(AssistantTelemetry.Attributes.Outcome));
        Assert.Equal("demo", turn.GetTagItem(AssistantTelemetry.Attributes.Provider));
        // Unknown tokens are absent, not zero.
        Assert.Null(turn.GetTagItem(AssistantTelemetry.Attributes.InputTokens));

        var everything = tools.Append(turn).ToList();
        Assert.All(
            everything.SelectMany(s => s.TagObjects),
            tag => Assert.Contains(tag.Key, AssistantTelemetry.Attributes.All)
        );
        var values = string.Join(
            "|",
            everything.SelectMany(s => s.TagObjects).Select(t => t.Value?.ToString())
        );
        foreach (var secret in new[] { Secretish, code, customer.Email, customer.UserId })
            Assert.DoesNotContain(secret, values);
        Assert.Empty(everything.SelectMany(s => s.Events));
    }

    [Fact]
    public async Task Export_reaches_an_otlp_collector_without_content_and_an_unreachable_one_does_not_block()
    {
        using var collector = new FakeCollector();
        await using (var host = Host(otlpEndpoint: collector.Url))
        {
            var customer = await AccountAsync(host);
            var code = await ActiveCouponCodeAsync(customer);
            var (correlationId, _) = await ChatAsync(
                customer.Client,
                $"Pokaż moje kupony {Secretish}"
            );
            Assert.True(host.Services.GetRequiredService<TracerProvider>().ForceFlush(10_000));

            var exported = collector.Bodies();
            Assert.True(exported.Length > 0, "nothing exported");
            Assert.Contains(correlationId, exported);
            Assert.Contains(AssistantTelemetry.TurnSpan, exported);
            foreach (var secret in new[] { Secretish, code, customer.Email, customer.UserId })
                Assert.DoesNotContain(secret, exported);
        }

        // Nothing listens on this port: the chat still answers at once.
        await using var down = Host(
            otlpEndpoint: $"http://127.0.0.1:{FakeCollector.FreePort()}/v1/traces"
        );
        var other = await AccountAsync(down);
        var watch = Stopwatch.StartNew();
        var (id, raw) = await ChatAsync(other.Client, "saldo");
        Assert.Contains("event: done", raw);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(TurnOutcome.Done, (await TraceAsync(id)).Outcome);
    }

    // ---- trace rows ----

    [Fact]
    public async Task Trace_row_has_metadata_timings_and_a_stable_keyed_pseudonym()
    {
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await using var host = Host(pseudonymKey: key);
        var customer = await AccountAsync(host);
        var second = await AccountAsync(host);

        var (first, _) = await ChatAsync(customer.Client, "saldo");
        var (again, _) = await ChatAsync(customer.Client, "oferty");
        var (otherUser, _) = await ChatAsync(second.Client, "saldo");

        var trace = await TraceAsync(first);
        var expected = Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Convert.FromBase64String(key),
                Encoding.UTF8.GetBytes(customer.UserId)
            )[..16]
        );
        Assert.Equal(expected, trace.UserPseudonym);
        Assert.Equal(expected, (await TraceAsync(again)).UserPseudonym);
        Assert.NotEqual(expected, (await TraceAsync(otherUser)).UserPseudonym);
        Assert.Equal(
            ("demo", "demo-rules", "demo-rules-v10", true),
            (trace.Provider, trace.Model, trace.PromptVersion, trace.Demo)
        );
        Assert.Equal(TurnOutcome.Done, trace.Outcome);
        Assert.Null(trace.ErrorCode);
        Assert.Equal(1, trace.ToolCalls);
        Assert.Equal("get_my_balance", Assert.Single(trace.Tools).Name);
        Assert.NotNull(trace.FirstEventMs);
        Assert.True(trace.TotalMs >= trace.FirstEventMs);
        Assert.Null(trace.InputTokens);
        Assert.Null(trace.OutputTokens);
    }

    [Fact]
    public async Task Reported_tokens_are_summed_and_an_abandoned_turn_is_traced_as_cancelled()
    {
        var steps = new ScriptedModel(
            (step, _) =>
                Task.FromResult<ModelStep>(
                    step == 0
                        ? new ModelStep.Tool(
                            new ToolCall(
                                AssistantTools.GetMyBalance,
                                JsonSerializer.SerializeToElement(new { })
                            )
                        )
                        {
                            Usage = new TokenUsage(100, 10),
                        }
                        : new ModelStep.Reply("ok") { Usage = new TokenUsage(150, 20) }
                )
        );
        await using (var host = Host(steps))
        {
            var customer = await AccountAsync(host);
            var (id, _) = await ChatAsync(customer.Client, "saldo");
            var counted = await TraceAsync(id);
            Assert.Equal((250, 30), (counted.InputTokens, counted.OutputTokens));
        }

        var started = new TaskCompletionSource();
        var slow = new ScriptedModel(
            async (_, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct);
                return new ModelStep.Reply("x");
            }
        );
        await using var slowHost = Host(slow);
        var leaving = await AccountAsync(slowHost);
        var conversationId = await leaving.Client.StartConversationAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { message = "saldo", conversationId }),
        };
        request.Headers.Add(
            TestHttpExtensions.CsrfHeader,
            await leaving.Client.GetCsrfTokenAsync()
        );
        using var abort = new CancellationTokenSource();
        var response = await leaving.Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            abort.Token
        );
        var correlationId = response.Headers.GetValues("X-Correlation-Id").Single();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await abort.CancelAsync();
        response.Dispose();

        AssistantTurn? trace = null;
        for (var i = 0; i < 50 && trace is null; i++)
        {
            trace = await DbAsync(db =>
                db.AssistantTurns.AsNoTracking()
                    .SingleOrDefaultAsync(t => t.CorrelationId == correlationId)
            );
            if (trace is null)
                await Task.Delay(100);
        }
        Assert.NotNull(trace);
        Assert.Equal(TurnOutcome.Cancelled, trace.Outcome);
        Assert.Null(trace.ErrorCode);
    }

    /// <summary>A minimal OTLP/HTTP endpoint that keeps the request bodies (protobuf, strings readable).</summary>
    private sealed class FakeCollector : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentQueue<byte[]> _bodies = new();

        public FakeCollector()
        {
            var port = FreePort();
            Url = $"http://127.0.0.1:{port}/v1/traces";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync();
                    }
                    catch
                    {
                        return;
                    }
                    using var buffer = new MemoryStream();
                    await context.Request.InputStream.CopyToAsync(buffer);
                    _bodies.Enqueue(buffer.ToArray());
                    context.Response.StatusCode = 200;
                    context.Response.Close();
                }
            });
        }

        public string Url { get; }

        public string Bodies() =>
            string.Join("\n", _bodies.Select(body => Encoding.UTF8.GetString(body)));

        public static int FreePort()
        {
            using var socket = new TcpListener(IPAddress.Loopback, 0);
            socket.Start();
            return ((IPEndPoint)socket.LocalEndpoint).Port;
        }

        public void Dispose() => _listener.Close();
    }
}

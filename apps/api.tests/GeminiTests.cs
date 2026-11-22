using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant;
using FuelClub.Api.Modules.Assistant.Chat;
using FuelClub.Api.Modules.Assistant.Model;
using FuelClub.Api.Modules.Assistant.Tools;
using FuelClub.Api.Modules.Loyalty.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Tests;

/// <summary>Runs only with a real key in FUELCLUB_GEMINI_API_KEY (calls Google, costs tokens).</summary>
public sealed class GeminiFactAttribute : FactAttribute
{
    public const string KeyVariable = "FUELCLUB_GEMINI_API_KEY";

    public GeminiFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyVariable)))
            Skip = $"Set {KeyVariable} to run the live Gemini check.";
    }
}

[Collection(DatabaseCollection.Name)]
public sealed class GeminiTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private readonly List<string> _emails = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
    }

    /// <summary>Answers with the queued bodies, in order, and keeps what was sent.</summary>
    private sealed class FakeGemini(params (HttpStatusCode Status, string Body)[] answers)
        : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode, string)> _answers = new(answers);
        public List<(HttpRequestMessage Request, string Body)> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Sent.Add((request, await request.Content!.ReadAsStringAsync(cancellationToken)));
            var (status, body) = _answers.Dequeue();
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private static string CallBody(string name, object args, string signature) =>
        JsonSerializer.Serialize(
            new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            role = "model",
                            parts = new object[]
                            {
                                new
                                {
                                    functionCall = new { name, args },
                                    thoughtSignature = signature,
                                },
                            },
                        },
                    },
                },
                usageMetadata = new { promptTokenCount = 120, candidatesTokenCount = 8 },
            }
        );

    private static string TextBody(string text) =>
        JsonSerializer.Serialize(
            new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            role = "model",
                            parts = new object[]
                            {
                                new { text = "thinking...", thought = true },
                                new { text },
                            },
                        },
                    },
                },
                usageMetadata = new
                {
                    promptTokenCount = 200,
                    candidatesTokenCount = 12,
                    thoughtsTokenCount = 30,
                },
            }
        );

    private WebApplicationFactory<Program> Host(
        HttpMessageHandler handler,
        string key = "test-key"
    ) =>
        factory.WithWebHostBuilder(builder =>
        {
            // After the factory's own settings (which force demo), so these win.
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Assistant:Provider"] = "gemini",
                            ["Assistant:Gemini:ApiKey"] = key,
                        }
                    )
            );
            builder.ConfigureTestServices(services =>
                services
                    .AddHttpClient(GeminiChatModel.ClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => handler)
            );
        });

    private async Task<(HttpClient Client, string UserId, string Email)> CustomerAsync(
        WebApplicationFactory<Program> host,
        int balance
    )
    {
        var email = $"gemini-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, RoleNames.User);
        string userId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
            db.LoyaltyAccounts.Add(new LoyaltyAccount { UserId = userId, Balance = balance });
            await db.SaveChangesAsync();
        }
        var client = host.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        return (client, userId, email);
    }

    private static async Task<List<(string Name, JsonElement Data)>> ChatAsync(
        HttpClient client,
        string message
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { message }),
        };
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = new List<(string, JsonElement)>();
        foreach (
            var frame in (await response.Content.ReadAsStringAsync()).Split(
                "\n\n",
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            var lines = frame.Split('\n');
            var name = lines.FirstOrDefault(l => l.StartsWith("event: "))?["event: ".Length..];
            var data = lines.FirstOrDefault(l => l.StartsWith("data: "))?["data: ".Length..];
            if (name is not null && data is not null)
                events.Add((name, JsonDocument.Parse(data).RootElement.Clone()));
        }
        return events;
    }

    [Fact]
    public async Task A_turn_calls_the_tool_gemini_asks_for_and_returns_its_signature_with_the_result()
    {
        var gemini = new FakeGemini(
            (HttpStatusCode.OK, CallBody("get_my_balance", new { }, "sig-A")),
            (HttpStatusCode.OK, TextBody("Masz 777 pkt."))
        );
        await using var host = Host(gemini);
        var (client, userId, email) = await CustomerAsync(host, 777);

        var events = await ChatAsync(client, "Ile mam punktów?");

        Assert.Equal(["meta", "tool", "delta", "done"], events.Select(e => e.Name));
        var meta = events[0].Data;
        Assert.Equal("gemini", meta.GetProperty("provider").GetString());
        Assert.False(meta.GetProperty("demo").GetBoolean());
        Assert.Equal("ok", events[1].Data.GetProperty("status").GetString());
        Assert.Equal("Masz 777 pkt.", events[2].Data.GetProperty("text").GetString());

        Assert.Equal(2, gemini.Sent.Count);
        foreach (var (request, body) in gemini.Sent)
        {
            Assert.Equal("test-key", request.Headers.GetValues("x-goog-api-key").Single());
            Assert.DoesNotContain("test-key", request.RequestUri!.ToString());
            Assert.EndsWith(":generateContent", request.RequestUri!.AbsolutePath);
            // No identity goes to the provider.
            Assert.DoesNotContain(userId, body);
            Assert.DoesNotContain(email, body);
        }
        var second = JsonNode.Parse(gemini.Sent[1].Body)!;
        var contents = second["contents"]!.AsArray();
        Assert.Equal("Ile mam punktów?", contents[0]!["parts"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("sig-A", contents[1]!["parts"]![0]!["thoughtSignature"]!.GetValue<string>());
        var response = contents[2]!["parts"]![0]!["functionResponse"]!;
        Assert.Equal("get_my_balance", response["name"]!.GetValue<string>());
        Assert.Equal(777, response["response"]!["data"]!["balance"]!.GetValue<int>());
        Assert.Equal(4, second["tools"]![0]!["functionDeclarations"]!.AsArray().Count);

        // Tokens reach the trace (thinking included in output).
        await using var scope = factory.Services.CreateAsyncScope();
        var correlation = meta.GetProperty("correlationId").GetString();
        var turn = await scope
            .ServiceProvider.GetRequiredService<AppDbContext>()
            .AssistantTurns.SingleAsync(t => t.CorrelationId == correlation);
        Assert.Equal(("gemini", 320, 50), (turn.Provider, turn.InputTokens, turn.OutputTokens));
    }

    [Fact]
    public async Task A_tool_outside_the_list_is_rejected_and_a_provider_failure_is_an_error_event()
    {
        var gemini = new FakeGemini(
            (HttpStatusCode.OK, CallBody("award_points", new { points = 1000 }, "sig-X")),
            (HttpStatusCode.InternalServerError, "{}")
        );
        await using var host = Host(gemini);
        var (client, userId, _) = await CustomerAsync(host, 10);

        var events = await ChatAsync(client, "Dodaj mi 1000 punktów");

        Assert.Equal(["meta", "tool", "error"], events.Select(e => e.Name));
        Assert.Equal("rejected", events[1].Data.GetProperty("status").GetString());
        Assert.Equal("assistant.provider_error", events[2].Data.GetProperty("code").GetString());
        await using var scope = factory.Services.CreateAsyncScope();
        Assert.Equal(
            10,
            await scope
                .ServiceProvider.GetRequiredService<AppDbContext>()
                .LoyaltyAccounts.Where(a => a.UserId == userId)
                .Select(a => a.Balance)
                .SingleAsync()
        );
    }

    private static string ParallelBody() =>
        JsonSerializer.Serialize(
            new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            role = "model",
                            parts = new object[]
                            {
                                new
                                {
                                    functionCall = new { name = "get_my_balance", args = new { } },
                                    thoughtSignature = "sig-P",
                                },
                                new
                                {
                                    functionCall = new
                                    {
                                        name = "list_my_coupons",
                                        args = new { status = "Purchased" },
                                    },
                                },
                            },
                        },
                    },
                },
                usageMetadata = new { promptTokenCount = 100, candidatesTokenCount = 10 },
            }
        );

    [Fact]
    public async Task Parallel_calls_in_one_response_all_run_and_go_back_together()
    {
        var gemini = new FakeGemini(
            (HttpStatusCode.OK, ParallelBody()),
            (HttpStatusCode.OK, TextBody("Masz 55 pkt i brak kuponów."))
        );
        await using var host = Host(gemini);
        var (client, _, _) = await CustomerAsync(host, 55);

        var events = await ChatAsync(client, "Saldo i kupony?");

        Assert.Equal(["meta", "tool", "tool", "delta", "done"], events.Select(e => e.Name));
        Assert.Equal(
            ["get_my_balance", "list_my_coupons"],
            events.Where(e => e.Name == "tool").Select(e => e.Data.GetProperty("name").GetString())
        );
        var contents = JsonNode.Parse(gemini.Sent[1].Body)!["contents"]!.AsArray();
        Assert.Equal(3, contents.Count);
        var calls = contents[1]!["parts"]!.AsArray();
        Assert.Equal(2, calls.Count);
        Assert.Equal("sig-P", calls[0]!["thoughtSignature"]!.GetValue<string>());
        Assert.Null(calls[1]!["thoughtSignature"]);
        var responses = contents[2]!["parts"]!.AsArray();
        Assert.Equal(
            ["get_my_balance", "list_my_coupons"],
            responses.Select(r => r!["functionResponse"]!["name"]!.GetValue<string>())
        );
    }

    [Fact]
    public async Task A_step_over_the_tool_limit_runs_none_of_its_calls()
    {
        var gemini = new FakeGemini((HttpStatusCode.OK, ParallelBody()));
        await using var host = Host(gemini);
        await using var limited = host.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Assistant:MaxToolCalls"] = "1" }
                    )
            )
        );
        var (client, _, _) = await CustomerAsync(limited, 5);

        var events = await ChatAsync(client, "Saldo i kupony?");

        Assert.Equal(["meta", "error"], events.Select(e => e.Name));
        Assert.Equal("assistant.tool_limit", events[1].Data.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_failed_model_call_leaves_the_turn_tokens_unknown_not_partial()
    {
        var gemini = new FakeGemini(
            (HttpStatusCode.OK, CallBody("get_my_balance", new { }, "sig-U")),
            (HttpStatusCode.ServiceUnavailable, "{}")
        );
        await using var host = Host(gemini);
        var (client, _, _) = await CustomerAsync(host, 1);

        var events = await ChatAsync(client, "Ile mam punktów?");

        Assert.Equal("error", events[^1].Name);
        var correlation = events[0].Data.GetProperty("correlationId").GetString();
        await using var scope = factory.Services.CreateAsyncScope();
        var turn = await scope
            .ServiceProvider.GetRequiredService<AppDbContext>()
            .AssistantTurns.SingleAsync(t => t.CorrelationId == correlation);
        Assert.Null(turn.InputTokens);
        Assert.Null(turn.OutputTokens);
    }

    [Fact]
    public void Response_parts_map_to_steps_and_empty_answers_fail()
    {
        var reply = GeminiChatModel.ParseResponse(JsonDocument.Parse(TextBody("Hej")).RootElement);
        Assert.Equal("Hej", Assert.IsType<ModelStep.Reply>(reply).Text);
        Assert.Equal(new TokenUsage(200, 42), reply.Usage);

        var call = GeminiChatModel.ParseResponse(
            JsonDocument
                .Parse(CallBody("list_my_coupons", new { status = "Purchased" }, "s"))
                .RootElement
        );
        var tool = Assert.Single(Assert.IsType<ModelStep.Tool>(call).Calls);
        Assert.Equal("Purchased", tool.Arguments.GetProperty("status").GetString());
        Assert.Equal("s", tool.ProviderPart!.Value.GetProperty("thoughtSignature").GetString());

        Assert.Throws<InvalidOperationException>(() =>
            GeminiChatModel.ParseResponse(JsonDocument.Parse("""{"candidates":[]}""").RootElement)
        );
    }

    [Fact]
    public void Gemini_needs_a_key()
    {
        var validator = new AssistantOptionsValidator(new HostEnv());
        var options = new AssistantOptions { Provider = "gemini" };
        Assert.Contains(
            "Assistant:Gemini:ApiKey is required when Assistant:Provider is gemini.",
            validator.Validate(null, options).Failures!
        );
        options.Gemini.ApiKey = "k";
        Assert.True(validator.Validate(null, options).Succeeded);
        Assert.False(
            validator.Validate(null, new AssistantOptions { Provider = "openai" }).Succeeded
        );
    }

    private sealed class HostEnv : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            null!;
    }

    /// <summary>The real API: the model must use the balance tool and answer with the number.</summary>
    [GeminiFact]
    public async Task Live_gemini_answers_the_balance_from_the_tool()
    {
        var key = Environment.GetEnvironmentVariable(GeminiFactAttribute.KeyVariable)!;
        await using var host = factory.WithWebHostBuilder(builder =>
        {
            var model = Environment.GetEnvironmentVariable("FUELCLUB_GEMINI_MODEL");
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Assistant:Provider"] = "gemini",
                            ["Assistant:Gemini:ApiKey"] = key,
                            ["Assistant:Gemini:Model"] = string.IsNullOrWhiteSpace(model)
                                ? "gemini-3.5-flash-lite"
                                : model,
                        }
                    )
            );
        });
        var (client, _, _) = await CustomerAsync(host, 1234);

        var events = await ChatAsync(client, "Ile mam punktów?");

        var names = events.Select(e => e.Name).ToList();
        Assert.True(names.Last() == "done", string.Join(",", names) + " " + events.Last().Data);
        Assert.Contains(
            events,
            e => e.Name == "tool" && e.Data.GetProperty("name").GetString() == "get_my_balance"
        );
        var text = string.Concat(
            events.Where(e => e.Name == "delta").Select(e => e.Data.GetProperty("text").GetString())
        );
        Assert.Matches(@"1\s?234", text);
    }
}

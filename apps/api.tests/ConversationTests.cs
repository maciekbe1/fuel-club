using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant.Conversations;
using FuelClub.Api.Modules.Assistant.Tracing;
using FuelClub.Api.Modules.Loyalty.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

/// <summary>Server conversations (ADR 0017): owner, state, idle, end, handoff link, simulation marks.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ConversationTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private readonly List<string> _emails = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        factory.Clock.Reset();
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
    }

    private sealed record Conversation(
        Guid Id,
        DateTimeOffset StartedAt,
        DateTimeOffset? EndedAt,
        string? EndReason,
        int Turns
    );

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<HttpClient> AccountAsync(WebApplicationFactory<Program>? host = null)
    {
        var email = $"conversation-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, RoleNames.User);
        var client = (host ?? factory).CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        var userId = await DbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()
        );
        await DbAsync(async db =>
        {
            db.LoyaltyAccounts.Add(new LoyaltyAccount { UserId = userId, Balance = 100 });
            return await db.SaveChangesAsync();
        });
        return client;
    }

    private static async Task<HttpResponseMessage> StartAsync(HttpClient client, string key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat/conversations");
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        request.Headers.Add(Idempotency.HeaderName, key);
        return await client.SendAsync(request);
    }

    private static async Task<(HttpStatusCode Status, string? Code, string Raw)> TurnAsync(
        HttpClient client,
        Guid conversationId,
        string message = "saldo"
    )
    {
        var response = await client.PostWithCsrfAsync("/api/chat", new { message, conversationId });
        var raw = await response.Content.ReadAsStringAsync();
        string? code = null;
        if (response.Content.Headers.ContentType?.MediaType == "application/problem+json")
            code = JsonDocument.Parse(raw).RootElement.GetProperty("code").GetString();
        return (response.StatusCode, code, raw);
    }

    private static async Task<HttpResponseMessage> EndAsync(HttpClient client, Guid id) =>
        await client.PostWithCsrfAsync($"/api/chat/conversations/{id}/end", new { });

    private Task<AssistantConversation> RowAsync(Guid id) =>
        DbAsync(db => db.AssistantConversations.AsNoTracking().SingleAsync(c => c.Id == id));

    private Task<List<AssistantTurn>> TurnsAsync(Guid id) =>
        DbAsync(db =>
            db.AssistantTurns.AsNoTracking().Where(t => t.ConversationId == id).ToListAsync()
        );

    [Fact]
    public async Task A_retried_start_with_the_same_key_returns_the_same_conversation()
    {
        var client = await AccountAsync();
        var key = Guid.NewGuid().ToString();

        var first = await StartAsync(client, key);
        var retry = await StartAsync(client, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var id = (await first.Content.ReadFromJsonAsync<Conversation>())!.Id;
        Assert.Equal(id, (await retry.Content.ReadFromJsonAsync<Conversation>())!.Id);
        var owner = (await RowAsync(id)).UserPseudonym;
        Assert.Equal(
            1,
            await DbAsync(db => db.AssistantConversations.CountAsync(c => c.UserPseudonym == owner))
        );
    }

    [Fact]
    public async Task Turns_carry_the_conversation_and_count_on_it()
    {
        var client = await AccountAsync();
        var id = await client.StartConversationAsync();

        var first = await TurnAsync(client, id);
        var second = await TurnAsync(client, id, "Pokaż kupony");

        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Contains($"\"conversationId\":\"{id}\"", first.Raw);
        Assert.Equal(2, (await RowAsync(id)).Turns);
        var turns = await TurnsAsync(id);
        Assert.Equal(2, turns.Count);
        Assert.All(turns, t => Assert.False(t.Synthetic));
        Assert.All(turns, t => Assert.Null(t.RunId));
        // Demo: model calls counted, cost not applicable (no price version).
        Assert.All(turns, t => Assert.True(t.ModelSteps >= 1));
        Assert.All(turns, t => Assert.Null(t.CostUsd));
        Assert.All(turns, t => Assert.Null(t.PriceVersion));
    }

    [Fact]
    public async Task A_turn_without_a_conversation_is_rejected()
    {
        var client = await AccountAsync();

        var response = await client.PostWithCsrfAsync("/api/chat", new { message = "saldo" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "assistant.required_field",
            problem
                .GetProperty("errors")
                .GetProperty("conversationId")[0]
                .GetProperty("code")
                .GetString()
        );
    }

    [Fact]
    public async Task Someone_elses_or_an_unknown_conversation_is_not_found_and_never_reaches_the_model()
    {
        var owner = await AccountAsync();
        var intruder = await AccountAsync();
        var id = await owner.StartConversationAsync();

        var foreign = await TurnAsync(intruder, id);
        var unknown = await TurnAsync(intruder, Guid.CreateVersion7());
        var foreignEnd = await EndAsync(intruder, id);

        Assert.Equal(
            (HttpStatusCode.NotFound, "assistant.conversation_not_found"),
            (foreign.Status, foreign.Code)
        );
        Assert.Equal(
            (HttpStatusCode.NotFound, "assistant.conversation_not_found"),
            (unknown.Status, unknown.Code)
        );
        Assert.Equal(HttpStatusCode.NotFound, foreignEnd.StatusCode);
        var row = await RowAsync(id);
        Assert.Equal(0, row.Turns);
        Assert.Null(row.EndedAt);
        Assert.Empty(await TurnsAsync(id));
    }

    [Fact]
    public async Task Ending_is_idempotent_and_an_ended_conversation_takes_no_turns()
    {
        var client = await AccountAsync();
        var id = await client.StartConversationAsync();
        await TurnAsync(client, id);

        var first = await (await EndAsync(client, id)).Content.ReadFromJsonAsync<Conversation>();
        var again = await EndAsync(client, id);
        var turn = await TurnAsync(client, id);

        Assert.Equal("Client", first!.EndReason);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(first, await again.Content.ReadFromJsonAsync<Conversation>());
        Assert.Equal(
            (HttpStatusCode.Conflict, "assistant.conversation_ended"),
            (turn.Status, turn.Code)
        );
        Assert.Single(await TurnsAsync(id));
    }

    [Fact]
    public async Task An_idle_conversation_ends_as_idle_not_as_the_clients_end()
    {
        var client = await AccountAsync();
        // PostgreSQL keeps microseconds; Linux clocks have 100 ns ticks, so a raw time comes back
        // shorter and an exact comparison fails in CI only.
        var start = Microseconds(DateTimeOffset.UtcNow.AddMinutes(1));
        factory.Clock.Set(start);
        var id = await client.StartConversationAsync();
        var timedOut = await client.StartConversationAsync();

        factory.Clock.Set(start.AddMinutes(31));
        var turn = await TurnAsync(client, id);
        var end = await (
            await EndAsync(client, timedOut)
        ).Content.ReadFromJsonAsync<Conversation>();

        Assert.Equal(
            (HttpStatusCode.Conflict, "assistant.conversation_ended"),
            (turn.Status, turn.Code)
        );
        var row = await RowAsync(id);
        Assert.Equal(ConversationEndReason.Idle, row.EndReason);
        // Ended when the limit ran out, not when it was noticed.
        Assert.Equal(start.AddMinutes(30), row.EndedAt);
        Assert.Equal("Idle", end!.EndReason);
        Assert.Null(row.SupportCaseId);
    }

    [Fact]
    public async Task A_case_links_its_conversation_once_and_retries_do_not_count_again()
    {
        var client = await AccountAsync();
        var id = await client.StartConversationAsync();
        await TurnAsync(client, id, "Chcę rozmawiać z konsultantem");
        var key = Guid.NewGuid().ToString();

        var created = await CreateCaseAsync(client, id, key);
        var retry = await CreateCaseAsync(client, id, key);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        var caseId = (await created.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("id")
            .GetGuid();
        var row = await RowAsync(id);
        Assert.Equal(caseId, row.SupportCaseId);
        Assert.Equal(ConversationEndReason.Handoff, row.EndReason);
        Assert.True(Assert.Single(await TurnsAsync(id)).HandoffProposed);

        // A later case (after the first one closed) names the same conversation: the first link stays.
        await client.PostWithCsrfAsync($"/api/support/cases/{caseId}/close", new { });
        var second = await CreateCaseAsync(client, id, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(caseId, (await RowAsync(id)).SupportCaseId);
        Assert.Equal(
            1,
            await DbAsync(db =>
                db.AssistantConversations.CountAsync(c => c.SupportCaseId == caseId)
            )
        );
    }

    [Fact]
    public async Task A_case_from_an_idle_conversation_links_it_but_it_stays_ended_as_idle()
    {
        var client = await AccountAsync();
        // PostgreSQL keeps microseconds; Linux clocks have 100 ns ticks, so a raw time comes back
        // shorter and an exact comparison fails in CI only.
        var start = Microseconds(DateTimeOffset.UtcNow.AddMinutes(1));
        factory.Clock.Set(start);
        var id = await client.StartConversationAsync();

        factory.Clock.Set(start.AddMinutes(60));
        var created = await CreateCaseAsync(client, id, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var row = await RowAsync(id);
        Assert.NotNull(row.SupportCaseId);
        Assert.Equal(ConversationEndReason.Idle, row.EndReason);
        Assert.Equal(start.AddMinutes(30), row.EndedAt);
    }

    [Fact]
    public async Task A_case_cannot_link_someone_elses_conversation()
    {
        var owner = await AccountAsync();
        var intruder = await AccountAsync();
        var id = await owner.StartConversationAsync();

        var response = await CreateCaseAsync(intruder, id, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "support.conversation_not_found",
            problem
                .GetProperty("errors")
                .GetProperty("conversationId")[0]
                .GetProperty("code")
                .GetString()
        );
        Assert.Null((await RowAsync(id)).SupportCaseId);
        Assert.Null((await RowAsync(id)).EndedAt);
    }

    private static async Task<HttpResponseMessage> CreateCaseAsync(
        HttpClient client,
        Guid conversationId,
        string key
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/support/cases")
        {
            Content = JsonContent.Create(new { message = "Poproszę człowieka", conversationId }),
        };
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        request.Headers.Add(Idempotency.HeaderName, key);
        return await client.SendAsync(request);
    }

    [Fact]
    public async Task Simulation_marks_come_from_the_instance_configuration_not_the_request()
    {
        await using var simulation = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Simulation:Enabled"] = "true",
                            ["Simulation:RunId"] = "conv-test",
                        }
                    )
            )
        );
        var synthetic = await AccountAsync(simulation);
        var real = await AccountAsync();

        var simulated = await synthetic.StartConversationAsync();
        await TurnAsync(synthetic, simulated);
        var plain = await real.StartConversationAsync();
        // A client cannot mark its own traffic.
        await real.PostWithCsrfAsync(
            "/api/chat",
            new
            {
                message = "saldo",
                conversationId = plain,
                synthetic = true,
                runId = "fake",
            }
        );

        var simulatedRow = await RowAsync(simulated);
        Assert.True(simulatedRow.Synthetic);
        Assert.Equal("conv-test", simulatedRow.RunId);
        var simulatedTurn = Assert.Single(await TurnsAsync(simulated));
        Assert.True(simulatedTurn.Synthetic);
        Assert.Equal("conv-test", simulatedTurn.RunId);
        var plainRow = await RowAsync(plain);
        Assert.False(plainRow.Synthetic);
        Assert.Null(plainRow.RunId);
        var plainTurn = Assert.Single(await TurnsAsync(plain));
        Assert.False(plainTurn.Synthetic);
        Assert.Null(plainTurn.RunId);
    }

    private static DateTimeOffset Microseconds(DateTimeOffset time) =>
        new(time.Ticks - time.Ticks % TimeSpan.TicksPerMicrosecond, time.Offset);
}

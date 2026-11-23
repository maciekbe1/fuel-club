using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FuelClub.Api.Tests;

/// <summary>Messages, the cursor, release and close (ADR 0012); every agent write re-checks the assignment.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SupportConversationTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Cases = "/api/support/cases";
    private const string Agent = "/api/support/agent/cases";
    private readonly List<string> _emails = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
    }

    private sealed record Account(HttpClient Client, string UserId, string Name);

    private sealed record CaseBody(Guid Id, string Status);

    private sealed record MessageItem(
        int Seq,
        string Author,
        bool ClientProvidedHistory,
        string? HistoryRole,
        string Text
    );

    private sealed record MessagesBody(
        Guid CaseId,
        string Status,
        int LastSeq,
        List<MessageItem> Items
    );

    private sealed record CreatedBody(int Seq);

    private sealed record QueueItem(
        Guid Id,
        string Status,
        string CustomerName,
        string FirstMessage
    );

    private sealed record QueueBody(List<QueueItem> Waiting, List<QueueItem> Mine);

    private sealed record ProblemBody(string? Code);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<Account> AccountAsync(string role = RoleNames.User)
    {
        var email = $"support-chat-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, role);
        var name = $"Klient {Guid.NewGuid():N}"[..14];
        var userId = await DbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == email);
            user.DisplayName = name;
            await db.SaveChangesAsync();
            return user.Id;
        });
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        return new Account(client, userId, name);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        Account account,
        string url,
        object? body = null,
        string? key = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        if (key is not null)
            request.Headers.Add(Idempotency.HeaderName, key);
        request.Headers.Add(
            TestHttpExtensions.CsrfHeader,
            await account.Client.GetCsrfTokenAsync()
        );
        return await account.Client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> SayAsync(
        Account account,
        string url,
        string text,
        string? key = null
    ) => PostAsync(account, url, new { text }, key ?? Guid.NewGuid().ToString());

    private static async Task<MessagesBody> ReadAsync(Account account, string url, int after)
    {
        var response = await account.Client.GetAsync($"{url}?after={after}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MessagesBody>())!;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())?.Code;

    private async Task<(Account Customer, Account Agent, Guid CaseId)> ClaimedCaseAsync()
    {
        var customer = await AccountAsync();
        var agent = await AccountAsync(RoleNames.Support);
        var created = await PostAsync(
            customer,
            Cases,
            new { message = "Pomocy" },
            Guid.NewGuid().ToString()
        );
        var caseId = (await created.Content.ReadFromJsonAsync<CaseBody>())!.Id;
        Assert.Equal(
            HttpStatusCode.OK,
            (await PostAsync(agent, $"{Agent}/{caseId}/claim")).StatusCode
        );
        return (customer, agent, caseId);
    }

    [Fact]
    public async Task Customer_and_agent_talk_through_the_cursor_and_the_queue_shows_the_customer_message()
    {
        var customer = await AccountAsync();
        var agent = await AccountAsync(RoleNames.Support);
        var created = await PostAsync(
            customer,
            Cases,
            new
            {
                message = "Kupon nie działa",
                history = new[] { new { role = "assistant", text = "Aktywowałem kupon" } },
            },
            Guid.NewGuid().ToString()
        );
        var caseId = (await created.Content.ReadFromJsonAsync<CaseBody>())!.Id;

        var current = await customer.Client.GetFromJsonAsync<CaseBody>($"{Cases}/current");
        Assert.Equal(caseId, current!.Id);
        var queue = (await agent.Client.GetFromJsonAsync<QueueBody>("/api/support/agent/queue"))!;
        var entry = Assert.Single(queue.Waiting, q => q.Id == caseId);
        Assert.Equal(customer.Name, entry.CustomerName);
        Assert.Equal("Kupon nie działa", entry.FirstMessage);
        // Not claimed: no reading yet.
        var early = await agent.Client.GetAsync($"{Agent}/{caseId}/messages");
        Assert.Equal(HttpStatusCode.Forbidden, early.StatusCode);
        Assert.Equal("support.not_assignee", await CodeOf(early));

        await PostAsync(agent, $"{Agent}/{caseId}/claim");
        queue = (await agent.Client.GetFromJsonAsync<QueueBody>("/api/support/agent/queue"))!;
        Assert.DoesNotContain(queue.Waiting, q => q.Id == caseId);
        Assert.Contains(queue.Mine, q => q.Id == caseId);

        var seen = await ReadAsync(agent, $"{Agent}/{caseId}/messages", 0);
        Assert.Equal("Assigned", seen.Status);
        Assert.Equal(
            [("Customer", true), ("Customer", false), ("System", false)],
            seen.Items.Select(m => (m.Author, m.ClientProvidedHistory))
        );
        Assert.Equal("assistant", seen.Items[0].HistoryRole);
        Assert.Equal(SupportSystemEvents.Claimed, seen.Items[2].Text);

        var answer = await SayAsync(agent, $"{Agent}/{caseId}/messages", "  Już sprawdzam  ");
        Assert.Equal(HttpStatusCode.Created, answer.StatusCode);
        Assert.Equal(4, (await answer.Content.ReadFromJsonAsync<CreatedBody>())!.Seq);

        var forCustomer = await ReadAsync(customer, $"{Cases}/{caseId}/messages", 3);
        var reply = Assert.Single(forCustomer.Items);
        Assert.Equal(("Support", "Już sprawdzam", 4), (reply.Author, reply.Text, reply.Seq));
        // The same cursor gives the same messages.
        Assert.Equal(
            forCustomer.Items,
            (await ReadAsync(customer, $"{Cases}/{caseId}/messages", 3)).Items
        );
        Assert.Empty((await ReadAsync(customer, $"{Cases}/{caseId}/messages", 4)).Items);

        Assert.Equal(
            HttpStatusCode.Created,
            (await SayAsync(customer, $"{Cases}/{caseId}/messages", "Dziękuję")).StatusCode
        );
        Assert.Equal(
            "Dziękuję",
            (await ReadAsync(agent, $"{Agent}/{caseId}/messages", 4)).Items.Single().Text
        );
    }

    [Fact]
    public async Task Strangers_see_nothing_and_invalid_input_is_refused()
    {
        var (customer, agent, caseId) = await ClaimedCaseAsync();
        var otherCustomer = await AccountAsync();
        var otherAgent = await AccountAsync(RoleNames.Support);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await otherCustomer.Client.GetAsync($"{Cases}/{caseId}/messages")).StatusCode
        );
        var foreignSend = await SayAsync(otherCustomer, $"{Cases}/{caseId}/messages", "x");
        Assert.Equal("support.case_not_found", await CodeOf(foreignSend));
        Assert.Equal(
            "support.not_assignee",
            await CodeOf(await otherAgent.Client.GetAsync($"{Agent}/{caseId}/messages"))
        );
        Assert.Equal(
            "support.not_assignee",
            await CodeOf(await SayAsync(otherAgent, $"{Agent}/{caseId}/messages", "x"))
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await otherCustomer.Client.GetAsync($"{Cases}/current")).StatusCode
        );
        // Each side only on its own paths.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await customer.Client.GetAsync($"{Agent}/{caseId}/messages")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await agent.Client.GetAsync($"{Cases}/{caseId}/messages")).StatusCode
        );

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await customer.Client.GetAsync($"{Cases}/{caseId}/messages?after=-1")).StatusCode
        );
        Assert.Equal(
            "support.validation_failed",
            await CodeOf(await SayAsync(customer, $"{Cases}/{caseId}/messages", "   "))
        );
        Assert.Equal(
            "support.validation_failed",
            await CodeOf(await SayAsync(agent, $"{Agent}/{caseId}/messages", new string('x', 1001)))
        );
    }

    [Fact]
    public async Task After_a_release_the_former_assignee_reads_and_writes_nothing_and_another_agent_takes_over()
    {
        var (_, first, caseId) = await ClaimedCaseAsync();
        var second = await AccountAsync(RoleNames.Support);

        var released = await PostAsync(first, $"{Agent}/{caseId}/release");
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal("Waiting", (await released.Content.ReadFromJsonAsync<CaseBody>())!.Status);

        foreach (
            var refused in new[]
            {
                await SayAsync(first, $"{Agent}/{caseId}/messages", "Jeszcze jedno"),
                await first.Client.GetAsync($"{Agent}/{caseId}/messages"),
                await PostAsync(first, $"{Agent}/{caseId}/release"),
                await PostAsync(first, $"{Agent}/{caseId}/close"),
            }
        )
        {
            Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
            Assert.Equal("support.not_assignee", await CodeOf(refused));
        }

        Assert.Equal(
            HttpStatusCode.OK,
            (await PostAsync(second, $"{Agent}/{caseId}/claim")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Created,
            (await SayAsync(second, $"{Agent}/{caseId}/messages", "Przejmuję")).StatusCode
        );
        Assert.Equal(
            "support.not_assignee",
            await CodeOf(await SayAsync(first, $"{Agent}/{caseId}/messages", "Ja też"))
        );

        var messages = await DbAsync(db =>
            db.SupportMessages.Where(m => m.CaseId == caseId).OrderBy(m => m.Seq).ToListAsync()
        );
        Assert.Equal(
            [
                SupportSystemEvents.Claimed,
                SupportSystemEvents.Released,
                SupportSystemEvents.Claimed,
            ],
            messages.Where(m => m.Author == SupportAuthor.System).Select(m => m.Text)
        );
        var support = Assert.Single(messages, m => m.Author == SupportAuthor.Support);
        Assert.Equal((second.UserId, "Przejmuję"), (support.AuthorUserId!, support.Text));
        Assert.Equal(Enumerable.Range(1, messages.Count), messages.Select(m => m.Seq));
    }

    [Fact]
    public async Task A_message_racing_a_release_waits_for_it_and_is_refused()
    {
        var (_, agent, caseId) = await ClaimedCaseAsync();

        Task<HttpResponseMessage> release,
            send;
        // The release has changed the row (and holds its lock) but has not committed.
        using (
            var gate = factory.LockGate.ArmWhen(command =>
                command.CommandText.Contains(
                    "SET \"Status\" = 'Waiting', \"AssignedToUserId\" = NULL",
                    StringComparison.Ordinal
                )
                && command
                    .Parameters.Cast<NpgsqlParameter>()
                    .Any(p => p.Value is Guid id && id == caseId)
            )
        )
        {
            release = PostAsync(agent, $"{Agent}/{caseId}/release");
            await gate.Acquired.WaitAsync(TimeSpan.FromSeconds(5));
            send = SayAsync(agent, $"{Agent}/{caseId}/messages", "Spóźniona odpowiedź");
            await WaitForRowLockWaiterAsync();
            Assert.False(send.IsCompleted);
            gate.Release();
        }

        Assert.Equal(HttpStatusCode.OK, (await release).StatusCode);
        var refused = await send;
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);
        Assert.Equal("support.not_assignee", await CodeOf(refused));
        Assert.Equal(
            0,
            await DbAsync(db =>
                db.SupportMessages.CountAsync(m =>
                    m.CaseId == caseId && m.Author == SupportAuthor.Support
                )
            )
        );
    }

    [Fact]
    public async Task A_closed_case_takes_no_messages_frees_the_chat_and_stays_readable()
    {
        var (customer, agent, caseId) = await ClaimedCaseAsync();

        foreach (var _ in new[] { 1, 2 })
        {
            var closed = await PostAsync(agent, $"{Agent}/{caseId}/close");
            Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
            Assert.Equal("Closed", (await closed.Content.ReadFromJsonAsync<CaseBody>())!.Status);
        }
        Assert.Equal(
            "support.case_closed",
            await CodeOf(await SayAsync(agent, $"{Agent}/{caseId}/messages", "x"))
        );
        Assert.Equal(
            "support.case_closed",
            await CodeOf(await SayAsync(customer, $"{Cases}/{caseId}/messages", "x"))
        );
        Assert.Equal(
            "support.case_closed",
            await CodeOf(await PostAsync(agent, $"{Agent}/{caseId}/release"))
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await customer.Client.GetAsync($"{Cases}/current")).StatusCode
        );

        var final = await ReadAsync(customer, $"{Cases}/{caseId}/messages", 0);
        Assert.Equal("Closed", final.Status);
        Assert.Equal(SupportSystemEvents.ClosedBySupport, final.Items[^1].Text);
        Assert.Single(final.Items, m => m.Text == SupportSystemEvents.ClosedBySupport);
        Assert.Equal("Closed", (await ReadAsync(agent, $"{Agent}/{caseId}/messages", 0)).Status);
    }

    [Fact]
    public async Task Parallel_messages_get_gapless_sequence_numbers_and_the_cursor_misses_none()
    {
        var (customer, agent, caseId) = await ClaimedCaseAsync();

        var sent = await Task.WhenAll(
            Enumerable
                .Range(0, 12)
                .Select(i =>
                    i % 2 == 0
                        ? SayAsync(customer, $"{Cases}/{caseId}/messages", $"k{i}")
                        : SayAsync(agent, $"{Agent}/{caseId}/messages", $"s{i}")
                )
        );
        Assert.All(sent, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var seqs = new List<int>();
        foreach (var response in sent)
            seqs.Add((await response.Content.ReadFromJsonAsync<CreatedBody>())!.Seq);
        // Claimed case: 1 = the first message, 2 = claimed.
        Assert.Equal(Enumerable.Range(3, 12), seqs.Order());

        // Reading in small steps from each returned cursor collects every message once.
        var collected = new List<MessageItem>();
        var cursor = 0;
        while (true)
        {
            var page = await ReadAsync(customer, $"{Cases}/{caseId}/messages", cursor);
            if (page.Items.Count == 0)
                break;
            collected.AddRange(page.Items.Take(5));
            cursor = collected[^1].Seq;
        }
        Assert.Equal(Enumerable.Range(1, 14), collected.Select(m => m.Seq));
    }

    [Fact]
    public async Task The_same_key_stores_a_message_once()
    {
        var (customer, agent, caseId) = await ClaimedCaseAsync();
        var key = Guid.NewGuid().ToString();

        var first = await SayAsync(customer, $"{Cases}/{caseId}/messages", "Raz", key);
        var replay = await SayAsync(customer, $"{Cases}/{caseId}/messages", "Raz", key);
        var agentKey = Guid.NewGuid().ToString();
        await SayAsync(agent, $"{Agent}/{caseId}/messages", "Dwa", agentKey);
        await SayAsync(agent, $"{Agent}/{caseId}/messages", "Dwa", agentKey);

        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<CreatedBody>())!.Seq,
            (await replay.Content.ReadFromJsonAsync<CreatedBody>())!.Seq
        );
        Assert.Equal(
            ["Pomocy", SupportSystemEvents.Claimed, "Raz", "Dwa"],
            await DbAsync(db =>
                db.SupportMessages.Where(m => m.CaseId == caseId)
                    .OrderBy(m => m.Seq)
                    .Select(m => m.Text)
                    .ToListAsync()
            )
        );
    }

    private async Task WaitForRowLockWaiterAsync()
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var waiting = await DbAsync(db =>
                db.Database.SqlQuery<int>(
                        $"""SELECT count(*)::int AS "Value" FROM pg_locks WHERE locktype <> 'advisory' AND NOT granted"""
                    )
                    .SingleAsync()
            );
            if (waiting > 0)
                return;
            await Task.Delay(10);
        }
        Assert.Fail("No request waited for the row lock.");
    }
}

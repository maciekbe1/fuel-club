using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Support;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

/// <summary>Expiry, the idle timeout and retention (ADR 0012), run directly with a chosen "now".</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SupportHousekeepingTests(ApiFactory factory) : IAsyncLifetime
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

    private sealed record Account(HttpClient Client, string UserId);

    private sealed record CaseBody(Guid Id, string Status);

    private sealed record ProblemBody(string? Code);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<HousekeepingResult> RunAsync(TimeSpan ahead)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<SupportHousekeeping>()
            .RunAsync(DateTimeOffset.UtcNow + ahead, CancellationToken.None);
    }

    private async Task<Account> AccountAsync(string role = RoleNames.User)
    {
        var email = $"support-keep-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, role);
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        var userId = await DbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()
        );
        return new Account(client, userId);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        Account account,
        string url,
        object? body = null,
        bool key = false
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        if (key)
            request.Headers.Add(Idempotency.HeaderName, Guid.NewGuid().ToString());
        request.Headers.Add(
            TestHttpExtensions.CsrfHeader,
            await account.Client.GetCsrfTokenAsync()
        );
        return await account.Client.SendAsync(request);
    }

    private static async Task<Guid> OpenCaseAsync(Account customer)
    {
        var created = await PostAsync(customer, Cases, new { message = "Pomocy" }, key: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<CaseBody>())!.Id;
    }

    private Task<SupportCase> CaseAsync(Guid id) =>
        DbAsync(db => db.SupportCases.AsNoTracking().SingleAsync(c => c.Id == id));

    private Task<List<string>> SystemEventsAsync(Guid id) =>
        DbAsync(db =>
            db.SupportMessages.Where(m => m.CaseId == id && m.Author == SupportAuthor.System)
                .OrderBy(m => m.Seq)
                .Select(m => m.Text)
                .ToListAsync()
        );

    [Fact]
    public async Task An_idle_assigned_case_goes_back_to_the_queue_and_the_former_agent_cannot_write()
    {
        var customer = await AccountAsync();
        var agent = await AccountAsync(RoleNames.Support);
        var caseId = await OpenCaseAsync(customer);
        await PostAsync(agent, $"{Agent}/{caseId}/claim");

        // Within the timeout nothing changes.
        await RunAsync(TimeSpan.FromHours(23));
        Assert.Equal(SupportCaseStatus.Assigned, (await CaseAsync(caseId)).Status);

        await RunAsync(TimeSpan.FromHours(25));
        var returned = await CaseAsync(caseId);
        Assert.Equal(SupportCaseStatus.Waiting, returned.Status);
        Assert.Null(returned.AssignedToUserId);
        Assert.Equal(
            [SupportSystemEvents.Claimed, SupportSystemEvents.IdleReturned],
            await SystemEventsAsync(caseId)
        );
        var late = await PostAsync(
            agent,
            $"{Agent}/{caseId}/messages",
            new { text = "Jestem" },
            key: true
        );
        Assert.Equal(HttpStatusCode.Forbidden, late.StatusCode);
        Assert.Equal(
            "support.not_assignee",
            (await late.Content.ReadFromJsonAsync<ProblemBody>())!.Code
        );
        // Back in the queue: the expiry clock starts again from the return.
        await RunAsync(TimeSpan.FromHours(25));
        Assert.Equal(SupportCaseStatus.Waiting, (await CaseAsync(caseId)).Status);
    }

    [Fact]
    public async Task A_waiting_case_without_activity_expires_frees_the_chat_and_allows_a_new_case()
    {
        var customer = await AccountAsync();
        var caseId = await OpenCaseAsync(customer);

        await RunAsync(TimeSpan.FromDays(6));
        Assert.Equal(SupportCaseStatus.Waiting, (await CaseAsync(caseId)).Status);

        await RunAsync(TimeSpan.FromDays(8));
        var expired = await CaseAsync(caseId);
        Assert.Equal(SupportCaseStatus.Expired, expired.Status);
        Assert.NotNull(expired.ClosedAt);
        Assert.Equal([SupportSystemEvents.Expired], await SystemEventsAsync(caseId));
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await customer.Client.GetAsync($"{Cases}/current")).StatusCode
        );
        var read = await customer.Client.GetFromJsonAsync<CaseBody>($"{Cases}/{caseId}/messages");
        Assert.Equal("Expired", read!.Status);
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await PostAsync(
                    customer,
                    $"{Cases}/{caseId}/messages",
                    new { text = "Halo" },
                    key: true
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Created,
            (await PostAsync(customer, Cases, new { message = "Znowu" }, key: true)).StatusCode
        );
    }

    [Fact]
    public async Task Closed_and_expired_cases_are_deleted_with_their_messages_after_the_retention()
    {
        var customer = await AccountAsync();
        var closedId = await OpenCaseAsync(customer);
        await PostAsync(customer, $"{Cases}/{closedId}/close");
        var other = await AccountAsync();
        var openId = await OpenCaseAsync(other);
        Assert.Equal(
            HttpStatusCode.Created,
            (
                await PostAsync(
                    other,
                    $"{Cases}/{openId}/messages",
                    new { text = "Czekam" },
                    key: true
                )
            ).StatusCode
        );

        // 29 days on: the waiting one expires (no activity for 7 days), both are kept.
        await RunAsync(TimeSpan.FromDays(29));
        Assert.Equal(SupportCaseStatus.Expired, (await CaseAsync(openId)).Status);
        Assert.True(await DbAsync(db => db.SupportCases.AnyAsync(c => c.Id == closedId)));

        // 31 days after closing and expiring (ClosedAt is the database clock, now): both go, messages too.
        await RunAsync(TimeSpan.FromDays(31));
        foreach (var id in new[] { closedId, openId })
        {
            Assert.False(await DbAsync(db => db.SupportCases.AnyAsync(c => c.Id == id)));
            Assert.False(await DbAsync(db => db.SupportMessages.AnyAsync(m => m.CaseId == id)));
        }
    }

    [Fact]
    public async Task Customer_messages_do_not_keep_a_case_with_an_absent_agent()
    {
        var customer = await AccountAsync();
        var agent = await AccountAsync(RoleNames.Support);
        var caseId = await OpenCaseAsync(customer);
        await PostAsync(agent, $"{Agent}/{caseId}/claim");
        // The agent's claim was 25 h ago; the customer wrote just now.
        await DbAsync(db =>
            db.SupportCases.Where(c => c.Id == caseId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(c => c.AgentActivityAt, DateTimeOffset.UtcNow.AddHours(-25))
                )
        );
        Assert.Equal(
            HttpStatusCode.Created,
            (
                await PostAsync(
                    customer,
                    $"{Cases}/{caseId}/messages",
                    new { text = "Halo?" },
                    key: true
                )
            ).StatusCode
        );

        await RunAsync(TimeSpan.Zero);
        Assert.Equal(SupportCaseStatus.Waiting, (await CaseAsync(caseId)).Status);

        // An agent message is activity: claimed again, it stays assigned.
        var next = await AccountAsync(RoleNames.Support);
        await PostAsync(next, $"{Agent}/{caseId}/claim");
        await DbAsync(db =>
            db.SupportCases.Where(c => c.Id == caseId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(c => c.AgentActivityAt, DateTimeOffset.UtcNow.AddHours(-25))
                )
        );
        await PostAsync(next, $"{Agent}/{caseId}/messages", new { text = "Jestem" }, key: true);
        await RunAsync(TimeSpan.Zero);
        Assert.Equal(SupportCaseStatus.Assigned, (await CaseAsync(caseId)).Status);
    }
}

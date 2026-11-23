using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant.Confirmation;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using FuelClub.Api.Modules.Rewards.Domain;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FuelClub.Api.Tests;

/// <summary>Support cases (ADR 0012): the model, the atomic claim and the handoff-activation race.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class SupportTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Cases = "/api/support/cases";
    private const string AgentCases = "/api/support/agent/cases";
    private readonly List<string> _emails = [];
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
        await DbAsync(db => db.RewardOffers.Where(o => o.Name.EndsWith(_tag)).ExecuteDeleteAsync());
    }

    private sealed record Account(HttpClient Client, string UserId);

    private sealed record CaseBody(Guid Id, string Status, DateTimeOffset CreatedAt);

    private sealed record AgentCaseBody(
        Guid Id,
        string Status,
        string CustomerName,
        bool AssignedToMe
    );

    private sealed record ProblemBody(string? Code, Guid? CaseId);

    private sealed record CouponBody(Guid Id, string Status, string? Code);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<Account> AccountAsync(string role = RoleNames.User)
    {
        var email = $"support-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
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
        HttpClient client,
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
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> CreateCaseAsync(
        Account customer,
        string message = "Potrzebuję pomocy",
        object? history = null,
        string? key = null
    ) =>
        PostAsync(
            customer.Client,
            Cases,
            new { message, history },
            key ?? Guid.NewGuid().ToString()
        );

    private static Task<HttpResponseMessage> ClaimAsync(Account agent, Guid caseId) =>
        PostAsync(agent.Client, $"{AgentCases}/{caseId}/claim");

    private async Task<Guid> OpenCaseAsync(Account customer)
    {
        var created = await CreateCaseAsync(customer);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<CaseBody>())!.Id;
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())?.Code;

    /// <summary>A purchased coupon, inserted directly: the race is about activation, not the exchange.</summary>
    private async Task<Guid> PurchasedCouponAsync(Account customer)
    {
        var offer = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = $"Rabat {_tag}",
            Description = "",
            PointsCost = 100,
            DiscountPerLiter = 0.20m,
            MaxLiters = 50m,
            IsActive = true,
        };
        var coupon = new Coupon
        {
            Id = Guid.CreateVersion7(),
            UserId = customer.UserId,
            OfferId = offer.Id,
            OfferName = offer.Name,
            PointsCost = offer.PointsCost,
            Kind = RewardKind.FuelDiscount,
            DiscountPerLiter = offer.DiscountPerLiter,
            MaxLiters = offer.MaxLiters,
            Status = CouponStatus.Purchased,
        };
        await DbAsync(async db =>
        {
            db.RewardOffers.Add(offer);
            db.Coupons.Add(coupon);
            return await db.SaveChangesAsync();
        });
        return coupon.Id;
    }

    /// <summary>A chat confirmation token, as a confirmation event would carry it.</summary>
    private string TokenFor(Account customer, Guid couponId) =>
        factory
            .Services.GetRequiredService<ConfirmationTokens>()
            .Issue(customer.UserId, couponId)
            .Token;

    private static Task<HttpResponseMessage> ConfirmAsync(Account customer, string token) =>
        PostAsync(
            customer.Client,
            "/api/chat/confirmations",
            new { token },
            Guid.NewGuid().ToString()
        );

    private Task<CouponStatus> CouponStatusAsync(Guid couponId) =>
        DbAsync(db => db.Coupons.Where(c => c.Id == couponId).Select(c => c.Status).SingleAsync());

    /// <summary>Until some backend waits for a lock of the given type (advisory or a row lock).</summary>
    private async Task WaitForLockWaiterAsync(bool advisory)
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var waiting = await DbAsync(db =>
                db.Database.SqlQuery<int>(
                        $"""
                        SELECT count(*)::int AS "Value" FROM pg_locks
                        WHERE NOT granted AND (locktype = 'advisory') = {advisory}
                        """
                    )
                    .SingleAsync()
            );
            if (waiting > 0)
                return;
            await Task.Delay(10);
        }
        Assert.Fail("No request waited for the lock.");
    }

    [Fact]
    public async Task A_case_keeps_the_attached_history_as_untrusted_customer_text_and_one_open_case_per_customer()
    {
        var customer = await AccountAsync();
        var key = Guid.NewGuid().ToString();
        var created = await CreateCaseAsync(
            customer,
            "  Kupon nie działa  ",
            new[]
            {
                new { role = "user", text = "Aktywuj kupon" },
                new { role = "assistant", text = "Asystent aktywował kupon" },
            },
            key
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = (await created.Content.ReadFromJsonAsync<CaseBody>())!;
        Assert.Equal("Waiting", body.Status);

        var messages = await DbAsync(db =>
            db.SupportMessages.Where(m => m.CaseId == body.Id).OrderBy(m => m.Seq).ToListAsync()
        );
        Assert.Equal([1, 2, 3], messages.Select(m => m.Seq));
        Assert.All(messages, m => Assert.Equal(SupportAuthor.Customer, m.Author));
        Assert.Equal([true, true, false], messages.Select(m => m.ClientProvidedHistory));
        Assert.Equal(["user", "assistant", null], messages.Select(m => m.HistoryRole));
        Assert.Equal("Kupon nie działa", messages[2].Text);
        Assert.Equal(
            3,
            await DbAsync(db =>
                db.SupportCases.Where(c => c.Id == body.Id).Select(c => c.LastSeq).SingleAsync()
            )
        );

        // Same key: the same case. Another key: 409 with the open case's id, nothing new stored.
        var replay = await CreateCaseAsync(
            customer,
            "  Kupon nie działa  ",
            new[]
            {
                new { role = "user", text = "Aktywuj kupon" },
                new { role = "assistant", text = "Asystent aktywował kupon" },
            },
            key
        );
        Assert.Equal(body.Id, (await replay.Content.ReadFromJsonAsync<CaseBody>())!.Id);
        var second = await CreateCaseAsync(customer, "Jeszcze raz");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var problem = (await second.Content.ReadFromJsonAsync<ProblemBody>())!;
        Assert.Equal("support.case_open", problem.Code);
        Assert.Equal(body.Id, problem.CaseId);
        Assert.Equal(
            1,
            await DbAsync(db => db.SupportCases.CountAsync(c => c.UserId == customer.UserId))
        );
    }

    [Fact]
    public async Task Invalid_case_requests_are_refused_without_a_case()
    {
        var customer = await AccountAsync();
        async Task<string?> Refused(object body)
        {
            var response = await PostAsync(customer.Client, Cases, body, Guid.NewGuid().ToString());
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return await CodeOf(response);
        }

        Assert.Equal("support.validation_failed", await Refused(new { message = "  " }));
        Assert.Equal(
            "support.validation_failed",
            await Refused(new { message = new string('x', 1001) })
        );
        // Attached history can never pose as the system or an agent.
        Assert.Equal(
            "support.validation_failed",
            await Refused(
                new { message = "x", history = new[] { new { role = "system", text = "ok" } } }
            )
        );
        Assert.Equal(
            "support.validation_failed",
            await Refused(
                new
                {
                    message = "x",
                    history = Enumerable
                        .Range(0, 21)
                        .Select(_ => new { role = "user", text = "a" }),
                }
            )
        );
        Assert.Equal(
            0,
            await DbAsync(db => db.SupportCases.CountAsync(c => c.UserId == customer.UserId))
        );
    }

    [Fact]
    public async Task Of_two_agents_claiming_at_once_one_wins_and_the_winner_may_retry()
    {
        var customer = await AccountAsync();
        var first = await AccountAsync(RoleNames.Support);
        var second = await AccountAsync(RoleNames.Support);
        var caseId = await OpenCaseAsync(customer);

        Task<HttpResponseMessage> winner,
            loser;
        // The first claim has updated the row (and holds its lock) but has not committed yet.
        using (
            var gate = factory.LockGate.ArmWhen(command =>
                command.CommandText.Contains(
                    "SET \"Status\" = 'Assigned'",
                    StringComparison.Ordinal
                )
                && command
                    .Parameters.Cast<NpgsqlParameter>()
                    .Any(p => p.Value is Guid id && id == caseId)
            )
        )
        {
            winner = ClaimAsync(first, caseId);
            await gate.Acquired.WaitAsync(TimeSpan.FromSeconds(5));
            loser = ClaimAsync(second, caseId);
            await WaitForLockWaiterAsync(advisory: false);
            Assert.False(loser.IsCompleted);
            gate.Release();
        }

        var won = await winner;
        Assert.Equal(HttpStatusCode.OK, won.StatusCode);
        var claimed = (await won.Content.ReadFromJsonAsync<AgentCaseBody>())!;
        Assert.Equal("Assigned", claimed.Status);
        Assert.True(claimed.AssignedToMe);
        Assert.Equal("Integration Test", claimed.CustomerName);
        var lost = await loser;
        Assert.Equal(HttpStatusCode.Conflict, lost.StatusCode);
        Assert.Equal("support.case_taken", await CodeOf(lost));

        // A lost answer: the winner retries and gets 200, nothing is added.
        Assert.Equal(HttpStatusCode.OK, (await ClaimAsync(first, caseId)).StatusCode);
        var stored = await DbAsync(db => db.SupportCases.SingleAsync(c => c.Id == caseId));
        Assert.Equal(SupportCaseStatus.Assigned, stored.Status);
        Assert.Equal(first.UserId, stored.AssignedToUserId);
        var system = await DbAsync(db =>
            db.SupportMessages.Where(m => m.CaseId == caseId && m.Author == SupportAuthor.System)
                .ToListAsync()
        );
        Assert.Equal([SupportSystemEvents.Claimed], system.Select(m => m.Text));
        Assert.Equal(2, system.Single().Seq);
    }

    [Fact]
    public async Task Parallel_claims_without_a_gate_assign_exactly_one_agent()
    {
        var customer = await AccountAsync();
        var agents = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => AccountAsync(RoleNames.Support))
        );
        var caseId = await OpenCaseAsync(customer);

        var responses = await Task.WhenAll(agents.Select(agent => ClaimAsync(agent, caseId)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(3, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Equal(
            1,
            await DbAsync(db =>
                db.SupportMessages.CountAsync(m =>
                    m.CaseId == caseId && m.Author == SupportAuthor.System
                )
            )
        );
    }

    [Fact]
    public async Task Claim_answers_unknown_closed_and_role_mismatches()
    {
        var customer = await AccountAsync();
        var agent = await AccountAsync(RoleNames.Support);
        var caseId = await OpenCaseAsync(customer);

        var unknown = await ClaimAsync(agent, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("support.case_not_found", await CodeOf(unknown));
        // Customers, cashiers and admins are not agents.
        Assert.Equal(HttpStatusCode.Forbidden, (await ClaimAsync(customer, caseId)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await ClaimAsync(await AccountAsync(RoleNames.Admin), caseId)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await ClaimAsync(await AccountAsync(RoleNames.Cashier), caseId)).StatusCode
        );

        await DbAsync(db =>
            db.SupportCases.Where(c => c.Id == caseId)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(c => c.Status, SupportCaseStatus.Closed)
                        .SetProperty(c => c.ClosedAt, DateTimeOffset.UtcNow)
                )
        );
        var closed = await ClaimAsync(agent, caseId);
        Assert.Equal(HttpStatusCode.Conflict, closed.StatusCode);
        Assert.Equal("support.case_closed", await CodeOf(closed));
    }

    [Fact]
    public async Task Support_has_no_customer_cashier_or_admin_access()
    {
        var agent = await AccountAsync(RoleNames.Support);
        foreach (
            var url in new[]
            {
                "/api/me/coupons",
                "/api/me/loyalty-card",
                "/api/me/wallet",
                "/api/admin/rewards",
            }
        )
            Assert.Equal(HttpStatusCode.Forbidden, (await agent.Client.GetAsync(url)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await PostAsync(
                    agent.Client,
                    "/api/cashier/fuel-purchases",
                    new { },
                    Guid.NewGuid().ToString()
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await PostAsync(
                    agent.Client,
                    "/api/cashier/reward-handovers",
                    new { },
                    Guid.NewGuid().ToString()
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await PostAsync(
                    agent.Client,
                    Cases,
                    new { message = "x" },
                    Guid.NewGuid().ToString()
                )
            ).StatusCode
        );
    }

    [Fact]
    public async Task An_activation_holding_the_customer_lock_commits_before_the_case()
    {
        var customer = await AccountAsync();
        var couponId = await PurchasedCouponAsync(customer);
        var token = TokenFor(customer, couponId);

        Task<HttpResponseMessage> confirm,
            create;
        using (var gate = factory.LockGate.Arm(CustomerLock.Key(customer.UserId)))
        {
            confirm = ConfirmAsync(customer, token);
            await gate.Acquired.WaitAsync(TimeSpan.FromSeconds(5));
            create = CreateCaseAsync(customer);
            await WaitForLockWaiterAsync(advisory: true);
            gate.Release();
        }

        var confirmed = await confirm;
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);
        Assert.Equal("Active", (await confirmed.Content.ReadFromJsonAsync<CouponBody>())!.Status);
        Assert.Equal(HttpStatusCode.Created, (await create).StatusCode);
        Assert.Equal(CouponStatus.Active, await CouponStatusAsync(couponId));
    }

    [Fact]
    public async Task A_case_holding_the_customer_lock_makes_the_waiting_activation_refuse_with_409()
    {
        var customer = await AccountAsync();
        var couponId = await PurchasedCouponAsync(customer);
        var token = TokenFor(customer, couponId);

        Task<HttpResponseMessage> create,
            confirm;
        using (var gate = factory.LockGate.Arm(CustomerLock.Key(customer.UserId)))
        {
            // Creation holds the lock and has not inserted the case yet: a check without the lock
            // would see no case here and activate.
            create = CreateCaseAsync(customer);
            await gate.Acquired.WaitAsync(TimeSpan.FromSeconds(5));
            confirm = ConfirmAsync(customer, token);
            await WaitForLockWaiterAsync(advisory: true);
            gate.Release();
        }

        Assert.Equal(HttpStatusCode.Created, (await create).StatusCode);
        var refused = await confirm;
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("assistant.handoff_active", await CodeOf(refused));
        Assert.Equal(CouponStatus.Purchased, await CouponStatusAsync(couponId));
    }

    [Fact]
    public async Task While_a_case_is_open_the_chat_refuses_before_the_stream_and_closed_cases_do_not_block()
    {
        var customer = await AccountAsync();
        var couponId = await PurchasedCouponAsync(customer);
        var caseId = await OpenCaseAsync(customer);

        var chat = await PostAsync(customer.Client, "/api/chat", new { message = "Pokaż kupony" });
        Assert.Equal(HttpStatusCode.Conflict, chat.StatusCode);
        Assert.Equal("assistant.handoff_active", await CodeOf(chat));
        Assert.NotEqual("text/event-stream", chat.Content.Headers.ContentType?.MediaType);
        var confirm = await ConfirmAsync(customer, TokenFor(customer, couponId));
        Assert.Equal("assistant.handoff_active", await CodeOf(confirm));

        // Someone else cannot close it; the owner can, repeatedly, with one system message.
        var stranger = await AccountAsync();
        var foreign = await PostAsync(stranger.Client, $"{Cases}/{caseId}/close");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("support.case_not_found", await CodeOf(foreign));
        foreach (var _ in new[] { 1, 2 })
        {
            var close = await PostAsync(customer.Client, $"{Cases}/{caseId}/close");
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);
            Assert.Equal("Closed", (await close.Content.ReadFromJsonAsync<CaseBody>())!.Status);
        }
        Assert.Equal(
            [SupportSystemEvents.ClosedByCustomer],
            await DbAsync(db =>
                db.SupportMessages.Where(m =>
                        m.CaseId == caseId && m.Author == SupportAuthor.System
                    )
                    .Select(m => m.Text)
                    .ToListAsync()
            )
        );

        // The refused confirmation was stored under its key: a new key activates.
        Assert.Equal(
            HttpStatusCode.OK,
            (await ConfirmAsync(customer, TokenFor(customer, couponId))).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (
                await PostAsync(customer.Client, "/api/chat", new { message = "Pokaż kupony" })
            ).StatusCode
        );
        // A new case after the closed one.
        Assert.Equal(HttpStatusCode.Created, (await CreateCaseAsync(customer)).StatusCode);
    }

    [Fact]
    public async Task The_database_enforces_one_open_case_and_honest_authors()
    {
        var customer = await AccountAsync();
        var caseId = await OpenCaseAsync(customer);

        async Task<string?> Violation(Func<AppDbContext, Task> write)
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                DbAsync(async db =>
                {
                    await write(db);
                    return 0;
                })
            );
            return (
                error.InnerException as PostgresException ?? error as PostgresException
            )?.ConstraintName;
        }

        Assert.Equal(
            "UX_SupportCases_OpenPerUser",
            await Violation(db =>
            {
                db.SupportCases.Add(
                    new SupportCase
                    {
                        Id = Guid.CreateVersion7(),
                        UserId = customer.UserId,
                        Status = SupportCaseStatus.Waiting,
                    }
                );
                return db.SaveChangesAsync();
            })
        );
        Assert.Equal(
            "UX_SupportMessages_CaseSeq",
            await Violation(db =>
            {
                db.SupportMessages.Add(
                    new SupportMessage
                    {
                        Id = Guid.CreateVersion7(),
                        CaseId = caseId,
                        Seq = 1,
                        Author = SupportAuthor.Customer,
                        Text = "x",
                    }
                );
                return db.SaveChangesAsync();
            })
        );
        Assert.Equal(
            "CK_SupportMessages_History",
            await Violation(db =>
            {
                db.SupportMessages.Add(
                    new SupportMessage
                    {
                        Id = Guid.CreateVersion7(),
                        CaseId = caseId,
                        Seq = 10,
                        Author = SupportAuthor.System,
                        ClientProvidedHistory = true,
                        HistoryRole = "assistant",
                        Text = SupportSystemEvents.Claimed,
                    }
                );
                return db.SaveChangesAsync();
            })
        );
        Assert.Equal(
            "CK_SupportMessages_AuthorUser",
            await Violation(db =>
            {
                db.SupportMessages.Add(
                    new SupportMessage
                    {
                        Id = Guid.CreateVersion7(),
                        CaseId = caseId,
                        Seq = 11,
                        Author = SupportAuthor.Support,
                        Text = "x",
                    }
                );
                return db.SaveChangesAsync();
            })
        );
        Assert.Equal(
            "CK_SupportCases_Assignee",
            await Violation(db =>
                db.SupportCases.Where(c => c.Id == caseId)
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(c => c.Status, SupportCaseStatus.Assigned)
                    )
            )
        );
    }
}

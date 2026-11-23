using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant.Conversations;
using FuelClub.Api.Modules.Assistant.Tracing;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Observability;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class ObservabilityTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Summary = "/api/admin/observability";
    private const string Review = "/api/admin/observability/review";

    // Synthetic data lives in its own far-future slot, so other tests' rows never fall in the window.
    private readonly DateTimeOffset _now = new DateTimeOffset(
        2099,
        1,
        1,
        12,
        0,
        0,
        TimeSpan.Zero
    ).AddMinutes(Random.Shared.Next(0, 500_000));
    private readonly List<string> _emails = [];
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        factory.Clock.Reset();
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
        await DbAsync(async db =>
        {
            await db
                .AssistantTurns.Where(t =>
                    t.StartedAt > _now.AddDays(-400) && t.StartedAt < _now.AddDays(1)
                )
                .ExecuteDeleteAsync();
            await db
                .AssistantConversations.Where(c =>
                    c.StartedAt > _now.AddDays(-400) && c.StartedAt < _now.AddDays(1)
                )
                .ExecuteDeleteAsync();
            await db
                .SecurityEvents.Where(e =>
                    e.OccurredAt > _now.AddDays(-400) && e.OccurredAt < _now.AddDays(1)
                )
                .ExecuteDeleteAsync();
            return await db.RewardOffers.Where(o => o.Name.Contains(_tag)).ExecuteDeleteAsync();
        });
    }

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<(HttpClient Client, string UserId, string Email)> AccountAsync(
        string role,
        WebApplicationFactory<Program>? host = null
    )
    {
        var email = $"observability-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, role);
        var client = (host ?? factory).CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        var userId = await DbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()
        );
        return (client, userId, email);
    }

    private string Pseudonym(string userId) =>
        factory.Services.GetRequiredService<TracePseudonyms>().For(userId);

    /// <summary>Waits until every expected kind is there: events are written after the response.</summary>
    private async Task<List<SecurityEvent>> EventsAsync(
        Func<SecurityEvent, bool> match,
        params SecurityEventKind[] kinds
    )
    {
        List<SecurityEvent> events = [];
        for (var i = 0; i < 60; i++)
        {
            events = (await DbAsync(db => db.SecurityEvents.AsNoTracking().ToListAsync()))
                .Where(match)
                .ToList();
            if (kinds.All(kind => events.Count(e => e.Kind == kind) >= kinds.Count(k => k == kind)))
                break;
            await Task.Delay(50);
        }
        return events;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string url,
        object? body,
        string? key = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add(Idempotency.HeaderName, key ?? Guid.NewGuid().ToString());
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    // ---- security events ----

    [Fact]
    public async Task Rejections_are_recorded_as_security_events_with_route_pattern_and_pseudonym_only()
    {
        var since = DateTimeOffset.UtcNow.AddSeconds(-1);
        var cashier = await AccountAsync(RoleNames.Cashier);
        var customer = await AccountAsync(RoleNames.User);
        await DbAsync(async db =>
        {
            db.LoyaltyAccounts.Add(new LoyaltyAccount { UserId = customer.UserId, Balance = 10 });
            return await db.SaveChangesAsync();
        });

        // 403: staff on customer data.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await cashier.Client.GetAsync("/api/me/wallet")).StatusCode
        );
        // 401 on login.
        var anonymous = factory.CreateClient();
        var login = await anonymous.PostWithCsrfAsync(
            "/api/auth/login",
            new { email = cashier.Email, password = "Wrong-Password-1!" }
        );
        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
        // 401 for "not signed in" is normal and not recorded.
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.GetAsync("/api/auth/me")).StatusCode
        );
        // 409 key reused: the same key, another offer.
        var offer = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = $"Zdarzenie {_tag}",
            Description = "",
            PointsCost = 1,
            DiscountPerLiter = 0.1m,
            MaxLiters = 10m,
            IsActive = true,
        };
        var redeemed = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = $"Drugie {_tag}",
            Description = "",
            PointsCost = 1,
            DiscountPerLiter = 0.1m,
            MaxLiters = 10m,
            IsActive = true,
        };
        await DbAsync(async db =>
        {
            db.RewardOffers.AddRange(offer, redeemed);
            return await db.SaveChangesAsync();
        });
        var key = Guid.NewGuid().ToString();
        Assert.Equal(
            HttpStatusCode.Created,
            (
                await PostAsync(
                    customer.Client,
                    $"/api/rewards/{offer.Id}/exchange",
                    new { version = 1 },
                    key
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await PostAsync(
                    customer.Client,
                    $"/api/rewards/{redeemed.Id}/exchange",
                    new { version = 1 },
                    key
                )
            ).StatusCode
        );
        // 409 coupon redeemed: a real redemption at the till, then the coupon is activated again.
        var coupon = await PostAsync(
            customer.Client,
            $"/api/rewards/{redeemed.Id}/exchange",
            new { version = 1 }
        );
        var couponId = (await coupon.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("coupon")
            .GetProperty("id")
            .GetGuid();
        var code = (
            await (
                await PostAsync(customer.Client, $"/api/me/coupons/{couponId}/activate", null)
            ).Content.ReadFromJsonAsync<JsonElement>()
        )
            .GetProperty("code")
            .GetString();
        var card = await customer.Client.IssueCardAsync();
        var tillEmail = $"observability-till-{Guid.NewGuid():N}@example.test";
        _emails.Add(tillEmail);
        var till = await factory.CreateCashierAsync(tillEmail);
        CashierTestHelpers.AssertCreated(
            await till.PurchaseAsync(CashierTestHelpers.Sale(card, code))
        );
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await PostAsync(customer.Client, $"/api/me/coupons/{couponId}/activate", null)
            ).StatusCode
        );

        var events = await EventsAsync(
            e =>
                e.OccurredAt >= since
                && (
                    e.UserPseudonym == Pseudonym(cashier.UserId)
                    || e.UserPseudonym == Pseudonym(customer.UserId)
                    || e.Route == "/api/auth/login"
                ),
            SecurityEventKind.AccessDenied,
            SecurityEventKind.LoginFailed,
            SecurityEventKind.IdempotencyConflict,
            SecurityEventKind.RepeatedRedemption
        );
        var mine = events.Where(e => e.UserPseudonym != null).ToList();
        Assert.Contains(
            mine,
            e =>
                e.Kind == SecurityEventKind.AccessDenied
                && e.Route == "/api/me/wallet"
                && e.Status == 403
                && e.UserPseudonym == Pseudonym(cashier.UserId)
        );
        Assert.Contains(
            mine,
            e =>
                e.Kind == SecurityEventKind.IdempotencyConflict
                && e.Code == "idempotency.key_reused"
                && e.Route == "/api/rewards/{id:guid}/exchange"
        );
        Assert.Contains(
            mine,
            e =>
                e.Kind == SecurityEventKind.RepeatedRedemption
                && e.Route.Contains("{id")
                && e.UserPseudonym == Pseudonym(customer.UserId)
        );
        Assert.Contains(
            events,
            e => e.Kind == SecurityEventKind.LoginFailed && e.UserPseudonym == null
        );
        Assert.DoesNotContain(events, e => e.Route == "/api/auth/me");
        // Neither ids from the URL nor identities are stored.
        Assert.All(
            events,
            e =>
            {
                Assert.DoesNotContain(couponId.ToString(), e.Route);
                Assert.NotEqual(customer.UserId, e.UserPseudonym);
            }
        );
    }

    [Fact]
    public async Task Login_and_purchases_have_rate_limits_and_hitting_one_is_recorded()
    {
        await using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["RateLimits:AuthPermitLimit"] = "2",
                            ["RateLimits:PurchasePermitLimit"] = "1",
                        }
                    )
            )
        );
        var since = DateTimeOffset.UtcNow.AddSeconds(-1);
        var anonymous = host.CreateClient();
        HttpStatusCode Attempt(HttpResponseMessage r) => r.StatusCode;
        var body = new { email = $"nobody-{_tag}@example.test", password = "Wrong-Password-1!" };
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            Attempt(await anonymous.PostWithCsrfAsync("/api/auth/login", body))
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            Attempt(await anonymous.PostWithCsrfAsync("/api/auth/login", body))
        );
        var limited = await anonymous.PostWithCsrfAsync("/api/auth/login", body);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.Contains("Retry-After"));

        // The limit is per client address from X-Forwarded-For (the Next proxy), not per socket.
        async Task<HttpStatusCode> From(string address, string url = "/api/auth/login")
        {
            var client = host.CreateClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", address);
            return (await client.PostWithCsrfAsync(url, body)).StatusCode;
        }
        Assert.Equal(HttpStatusCode.Unauthorized, await From("203.0.113.10"));
        // Trailing slash: still the login endpoint, still counted.
        Assert.Equal(HttpStatusCode.Unauthorized, await From("203.0.113.10", "/api/auth/login/"));
        Assert.Equal(HttpStatusCode.TooManyRequests, await From("203.0.113.10"));
        Assert.Equal(HttpStatusCode.Unauthorized, await From("203.0.113.11"));

        // The cashier and customer come through the main host (its login limit is not lowered).
        var cashierEmail = $"observability-cashier-{Guid.NewGuid():N}@example.test";
        _emails.Add(cashierEmail);
        await factory.CreateCashierAsync(cashierEmail);
        var customer = await AccountAsync(RoleNames.User);
        var card = await customer.Client.IssueCardAsync();
        // A fresh host for the purchase limit (its own limiter), login allowed there.
        await using var purchaseHost = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["RateLimits:PurchasePermitLimit"] = "1" }
                    )
            )
        );
        var till = purchaseHost.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await till.LoginAsync(cashierEmail, CashierTestHelpers.Password);
        CashierTestHelpers.AssertCreated(
            await till.PurchaseAsync(CashierTestHelpers.Sale(card, null))
        );
        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            (await till.PurchaseAsync(CashierTestHelpers.Sale(card, null))).StatusCode
        );

        var events = await EventsAsync(
            e => e.OccurredAt >= since && e.Kind == SecurityEventKind.RateLimited,
            SecurityEventKind.RateLimited,
            SecurityEventKind.RateLimited
        );
        Assert.Contains(
            events,
            e => e.Route == "/api/auth/login" && e.Code == "rate_limit.exceeded"
        );
        Assert.Contains(events, e => e.Route == "/api/cashier/fuel-purchases");
    }

    // ---- rules ----

    private async Task SeedAsync(params object[] rows)
    {
        await DbAsync(async db =>
        {
            foreach (var row in rows)
                db.Add(row);
            return await db.SaveChangesAsync();
        });
    }

    private SecurityEvent Event(SecurityEventKind kind, TimeSpan ago) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = _now - ago,
            Kind = kind,
            Status = 403,
            Route = "/test",
        };

    private AssistantTurn Turn(
        TurnOutcome outcome,
        string? code = null,
        string pseudonym = "p-test",
        TimeSpan? ago = null,
        string toolStatus = "ok",
        int totalMs = 100
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            CorrelationId = Guid.NewGuid().ToString("N"),
            UserPseudonym = pseudonym,
            Provider = "test",
            Model = "m",
            PromptVersion = "v",
            StartedAt = _now - (ago ?? TimeSpan.FromMinutes(1)),
            Outcome = outcome,
            ErrorCode = code,
            TotalMs = totalMs,
            FirstEventMs = totalMs / 2,
            ToolCalls = 1,
            Tools =
            [
                new TurnToolCall
                {
                    Name = "get_my_balance",
                    Status = toolStatus,
                    DurationMs = 3,
                },
            ],
        };

    private async Task<Dictionary<string, AnomalyResult>> EvaluateAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var rules = scope.ServiceProvider.GetRequiredService<AnomalyRules>();
        return (await rules.EvaluateAsync(_now, CancellationToken.None)).ToDictionary(r => r.Rule);
    }

    [Fact]
    public async Task Rules_count_inside_their_window_and_rate_rules_need_a_minimum_sample()
    {
        // repeated_redemption: threshold 3 in 15 min; one event outside the window.
        await SeedAsync(
            Event(SecurityEventKind.RepeatedRedemption, TimeSpan.FromMinutes(1)),
            Event(SecurityEventKind.RepeatedRedemption, TimeSpan.FromMinutes(2)),
            Event(SecurityEventKind.RepeatedRedemption, TimeSpan.FromMinutes(20))
        );
        var below = await EvaluateAsync();
        Assert.Equal(2, below[AnomalyRules.RepeatedRedemption].Value);
        Assert.False(below[AnomalyRules.RepeatedRedemption].Triggered);

        await SeedAsync(Event(SecurityEventKind.RepeatedRedemption, TimeSpan.FromMinutes(3)));
        Assert.True((await EvaluateAsync())[AnomalyRules.RepeatedRedemption].Triggered);

        // ai_errors: 100 % failed but only 3 finished turns (< MinSample 10): not judged.
        await SeedAsync(
            Turn(TurnOutcome.Error, "assistant.provider_error"),
            Turn(TurnOutcome.Error, "assistant.timeout"),
            Turn(TurnOutcome.Error, "assistant.provider_error")
        );
        var small = (await EvaluateAsync())[AnomalyRules.AiErrors];
        Assert.Equal((1.0, 3, false), (small.Value, small.Sample, small.Triggered));

        // 7 more done turns and 5 cancelled (a client leaving is not an AI error): 3/10 = 30 % >= 20 %.
        await SeedAsync(
            Enumerable
                .Range(0, 7)
                .Select(_ => (object)Turn(TurnOutcome.Done))
                .Concat(Enumerable.Range(0, 5).Select(_ => (object)Turn(TurnOutcome.Cancelled)))
                .ToArray()
        );
        var judged = (await EvaluateAsync())[AnomalyRules.AiErrors];
        Assert.Equal(
            (0.3, 10, true, "critical"),
            (judged.Value, judged.Sample, judged.Triggered, judged.Severity)
        );

        // tool_limit and rejected tools.
        await SeedAsync(
            Turn(TurnOutcome.Error, "assistant.tool_limit"),
            Turn(TurnOutcome.Error, "assistant.tool_limit"),
            Turn(TurnOutcome.Error, "assistant.tool_limit"),
            Turn(TurnOutcome.Done, toolStatus: "rejected")
        );
        var after = await EvaluateAsync();
        Assert.True(after[AnomalyRules.ToolLimit].Triggered);
        Assert.Equal(1, after[AnomalyRules.RejectedTools].Value);
        Assert.False(after[AnomalyRules.RejectedTools].Triggered);
    }

    [Fact]
    public void Stored_security_events_are_capped_per_minute()
    {
        var clock = new MutableTimeProvider();
        var budget = new SecurityEventBudget(
            Microsoft.Extensions.Options.Options.Create(
                new ObservabilityOptions { MaxSecurityEventsPerMinute = 2 }
            ),
            clock,
            NullLogger<SecurityEventBudget>.Instance
        );
        var minute = DateTimeOffset.FromUnixTimeSeconds(_now.ToUnixTimeSeconds() / 60 * 60);
        clock.Set(minute);
        Assert.Equal(
            [true, true, false, false],
            Enumerable.Range(0, 4).Select(_ => budget.TryTake())
        );
        clock.Set(minute.AddMinutes(1));
        Assert.True(budget.TryTake());
    }

    [Fact]
    public void Alerts_repeat_only_after_the_cooldown()
    {
        var alerts = new AnomalyAlerts(NullLogger<AnomalyAlerts>.Instance);
        var triggered = new AnomalyResult(
            "access_denied",
            "warning",
            true,
            25,
            20,
            25,
            1,
            15,
            true,
            null
        );
        var quiet = triggered with { Rule = "login_failed", Triggered = false };
        TimeSpan Cooldown(string _) => TimeSpan.FromMinutes(30);

        Assert.Equal(["access_denied"], alerts.Raise([triggered, quiet], Cooldown, _now));
        Assert.Empty(alerts.Raise([triggered], Cooldown, _now.AddMinutes(10)));
        Assert.Equal(["access_denied"], alerts.Raise([triggered], Cooldown, _now.AddMinutes(31)));
        Assert.Equal(_now.AddMinutes(31), alerts.LastAlertAt("access_denied"));
    }

    [Fact]
    public void Rule_configuration_is_validated()
    {
        var validator = new ObservabilityOptionsValidator();
        Assert.True(validator.Validate(null, new ObservabilityOptions()).Succeeded);
        var bad = new ObservabilityOptions();
        bad.Rules["ai_errors"] = new AnomalyRuleOptions { Threshold = 5, MinSample = 0 };
        bad.Rules["made_up"] = new AnomalyRuleOptions { Threshold = 1 };
        var result = validator.Validate(null, bad);
        Assert.False(result.Succeeded);
        Assert.Equal(3, result.Failures!.Count());
    }

    // ---- admin endpoints ----

    [Fact]
    public async Task Summary_is_admin_only_and_shows_aggregates_without_identities()
    {
        await SeedAsync(
            Turn(TurnOutcome.Done, totalMs: 100),
            Turn(TurnOutcome.Done, totalMs: 200),
            Turn(TurnOutcome.Error, "assistant.timeout", totalMs: 1000),
            Turn(TurnOutcome.Cancelled, totalMs: 50),
            Event(SecurityEventKind.AccessDenied, TimeSpan.FromMinutes(5))
        );
        factory.Clock.Set(_now);
        var admin = await AccountAsync(RoleNames.Admin);
        var customer = await AccountAsync(RoleNames.User);

        var response = await admin.Client.GetAsync($"{Summary}?hours=1");
        var raw = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var summary = JsonSerializer.Deserialize<ObservabilitySummary>(
            raw,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
        )!;
        Assert.Equal(
            (4, 2, 1, 1),
            (summary.Turns.Turns, summary.Turns.Done, summary.Turns.Errors, summary.Turns.Cancelled)
        );
        Assert.Equal("assistant.timeout", Assert.Single(summary.Turns.ErrorCodes).Key);
        Assert.Equal(880, summary.Turns.P95TotalMs); // percentile_cont: 200 + 0.85 x (1000 - 200)
        Assert.Equal(new ToolCount("get_my_balance", "ok", 4), Assert.Single(summary.Turns.Tools));
        Assert.Null(summary.Turns.InputTokens);
        Assert.Equal(4, summary.Turns.TurnsWithoutTokenData);
        Assert.Equal(new CountItem("AccessDenied", 1), Assert.Single(summary.Security));
        Assert.Equal(AnomalyRules.All, summary.Anomalies.Select(a => a.Rule));
        Assert.DoesNotContain(customer.UserId, raw);
        Assert.DoesNotContain("@", raw);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await customer.Client.GetAsync(Summary)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync(Summary)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await admin.Client.GetAsync($"{Summary}?hours=0")).StatusCode
        );
    }

    // ---- P16c metrics (ADR 0018) ----

    private AssistantTurn MetricTurn(
        string run,
        bool demo,
        int steps,
        decimal? cost = null,
        int? input = null,
        string model = "m",
        string prompt = "v",
        Guid? conversation = null,
        bool proposed = false
    )
    {
        var turn = Turn(TurnOutcome.Done);
        turn.Synthetic = true;
        turn.RunId = run;
        turn.Demo = demo;
        turn.Provider = demo ? "demo" : "gemini";
        turn.Model = model;
        turn.PromptVersion = prompt;
        turn.ModelSteps = steps;
        turn.CostUsd = cost;
        turn.PriceVersion = cost is null ? null : "price-test";
        turn.InputTokens = input;
        turn.OutputTokens = input is null ? null : 10;
        turn.ConversationId = conversation;
        turn.HandoffProposed = proposed;
        turn.ToolCalls = 2;
        turn.Tools.Add(
            new TurnToolCall
            {
                Name = "search_program_rules",
                Status = "no_match",
                DurationMs = 1,
            }
        );
        return turn;
    }

    private async Task<ObservabilitySummary> SummaryAsync(HttpClient admin, string query)
    {
        var response = await admin.GetAsync($"{Summary}?hours=1&{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ObservabilitySummary>())!;
    }

    [Fact]
    public async Task Turns_steps_and_tool_calls_are_separate_counters_and_cost_tells_demo_from_unknown()
    {
        var run = $"m-{_tag}";
        await SeedAsync(
            MetricTurn(run, demo: true, steps: 1),
            MetricTurn(run, demo: false, steps: 2, cost: 0.0015m, input: 1000),
            // Gemini without usage: unknown, not zero.
            MetricTurn(run, demo: false, steps: 3)
        );
        factory.Clock.Set(_now);
        var admin = (await AccountAsync(RoleNames.Admin)).Client;

        var synthetic = await SummaryAsync(admin, $"traffic=synthetic&runId={run}");
        var real = await SummaryAsync(admin, $"runId={run}");

        Assert.Equal(3, synthetic.Turns.Turns);
        Assert.Equal((6L, 3), (synthetic.Turns.ModelSteps, synthetic.Turns.TurnsWithStepData));
        Assert.Equal(6L, synthetic.Turns.ToolCalls);
        Assert.Equal(3L, synthetic.Turns.ToolCallsNotOk);
        Assert.Equal(1, synthetic.Turns.DemoTurns);
        Assert.Equal(1, synthetic.Turns.TurnsWithoutTokenData);
        Assert.Equal(
            (0.0015m, 1, 1, 1),
            (
                synthetic.Cost.Usd,
                synthetic.Cost.KnownTurns,
                synthetic.Cost.UnknownTurns,
                synthetic.Cost.NotApplicableTurns
            )
        );
        Assert.Equal(["price-test"], synthetic.Cost.PriceVersions);
        Assert.Contains(run, synthetic.Options.RunIds);
        // The real view (default) never shows synthetic traffic.
        Assert.Equal(0, real.Turns.Turns);
        Assert.Equal("real", real.Filters.Traffic);
    }

    [Fact]
    public async Task Demo_only_traffic_has_no_cost_at_all_not_a_zero_cost()
    {
        var run = $"d-{_tag}";
        await SeedAsync(
            MetricTurn(run, demo: true, steps: 1),
            MetricTurn(run, demo: true, steps: 1)
        );
        factory.Clock.Set(_now);
        var admin = (await AccountAsync(RoleNames.Admin)).Client;

        var summary = await SummaryAsync(admin, $"traffic=synthetic&runId={run}");

        Assert.Null(summary.Cost.Usd);
        Assert.Equal(
            (0, 0, 2),
            (summary.Cost.KnownTurns, summary.Cost.UnknownTurns, summary.Cost.NotApplicableTurns)
        );
        Assert.Equal(0, summary.Turns.TurnsWithoutTokenData);
    }

    [Fact]
    public async Task Model_and_prompt_filters_narrow_turns_and_conversations()
    {
        var run = $"f-{_tag}";
        var a = Conversation(run, ConversationEndReason.Client);
        var b = Conversation(run, ConversationEndReason.Client);
        await SeedAsync(
            a,
            b,
            MetricTurn(
                run,
                demo: true,
                steps: 1,
                model: "demo-rules",
                prompt: "v7",
                conversation: a.Id
            ),
            MetricTurn(
                run,
                demo: false,
                steps: 1,
                model: "gemini-x",
                prompt: "g7",
                conversation: b.Id
            )
        );
        factory.Clock.Set(_now);
        var admin = (await AccountAsync(RoleNames.Admin)).Client;

        var byModel = await SummaryAsync(admin, $"traffic=synthetic&runId={run}&model=gemini-x");
        var byPrompt = await SummaryAsync(admin, $"traffic=synthetic&runId={run}&promptVersion=v7");

        Assert.Equal((1, 1), (byModel.Turns.Turns, byModel.Conversations.Started));
        Assert.Equal(1, byModel.Turns.Turns - byModel.Turns.DemoTurns);
        Assert.Equal((1, 1), (byPrompt.Turns.Turns, byPrompt.Conversations.Started));
        Assert.Equal(1, byPrompt.Turns.DemoTurns);
        Assert.Equal(
            ["demo-rules", "gemini-x"],
            byModel.Options.Models.Where(m => m is "demo-rules" or "gemini-x")
        );
    }

    [Fact]
    public async Task Model_and_prompt_must_match_the_same_turn_of_a_conversation()
    {
        var run = $"g-{_tag}";
        var mixed = Conversation(run, ConversationEndReason.Client);
        await SeedAsync(
            mixed,
            MetricTurn(
                run,
                demo: false,
                steps: 1,
                model: "m-a",
                prompt: "p-x",
                conversation: mixed.Id
            ),
            MetricTurn(
                run,
                demo: false,
                steps: 1,
                model: "m-b",
                prompt: "p-y",
                conversation: mixed.Id
            )
        );
        factory.Clock.Set(_now);
        var admin = (await AccountAsync(RoleNames.Admin)).Client;

        // No turn has both: no turn and no conversation, never a conversation without turns.
        var crossed = await SummaryAsync(
            admin,
            $"traffic=synthetic&runId={run}&model=m-a&promptVersion=p-y"
        );
        var matching = await SummaryAsync(
            admin,
            $"traffic=synthetic&runId={run}&model=m-a&promptVersion=p-x"
        );

        Assert.Equal((0, 0), (crossed.Turns.Turns, crossed.Conversations.Started));
        Assert.Equal((1, 1), (matching.Turns.Turns, matching.Conversations.Started));
    }

    private AssistantConversation Conversation(
        string run,
        ConversationEndReason? end,
        TimeSpan? lastActivityAgo = null,
        Guid? supportCase = null
    )
    {
        var last = _now - (lastActivityAgo ?? TimeSpan.FromMinutes(2));
        return new AssistantConversation
        {
            Id = Guid.CreateVersion7(),
            UserPseudonym = "p-test",
            StartedAt = _now - TimeSpan.FromMinutes(50),
            LastActivityAt = last,
            EndedAt = end is null ? null : last,
            EndReason = end,
            Turns = 1,
            SupportCaseId = supportCase,
            Synthetic = true,
            RunId = run,
        };
    }

    [Fact]
    public async Task Handoff_counts_once_per_conversation_and_idle_is_not_a_resolution()
    {
        var run = $"c-{_tag}";
        var handedOver = Conversation(
            run,
            ConversationEndReason.Handoff,
            supportCase: Guid.NewGuid()
        );
        var declined = Conversation(run, ConversationEndReason.Client);
        var idle = Conversation(run, ConversationEndReason.Idle);
        // Past the idle limit but never touched again: idle already, though nobody closed it.
        var stale = Conversation(run, null, lastActivityAgo: TimeSpan.FromMinutes(45));
        var open = Conversation(run, null);
        var plain = Conversation(run, ConversationEndReason.Client);
        await SeedAsync(
            handedOver,
            declined,
            idle,
            stale,
            open,
            plain,
            // Two proposals in one conversation: one conversation with a proposal.
            MetricTurn(run, demo: true, steps: 1, conversation: handedOver.Id, proposed: true),
            MetricTurn(run, demo: true, steps: 1, conversation: handedOver.Id, proposed: true),
            MetricTurn(run, demo: true, steps: 1, conversation: declined.Id, proposed: true)
        );
        factory.Clock.Set(_now);
        var admin = (await AccountAsync(RoleNames.Admin)).Client;

        var summary = await SummaryAsync(admin, $"traffic=synthetic&runId={run}");

        Assert.Equal(
            new ConversationMetrics(
                Started: 6,
                Open: 1,
                Ended: 5,
                EndedByClient: 2,
                EndedIdle: 2,
                EndedByHandoff: 1,
                WithHandoffProposal: 2,
                WithCase: 1,
                EndedWithoutCase: 4
            ),
            summary.Conversations
        );
    }

    [Theory]
    [InlineData("traffic=bogus")]
    [InlineData("runId=")]
    public async Task Bad_filters_are_rejected(string query)
    {
        var admin = (await AccountAsync(RoleNames.Admin)).Client;

        var response = await admin.GetAsync($"{Summary}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Review_queue_lists_suspicious_turns_flags_repeated_errors_and_can_be_cleared()
    {
        var pseudonym = $"p-{_tag}";
        var failed = Turn(TurnOutcome.Error, "assistant.provider_error", pseudonym);
        var rejected = Turn(TurnOutcome.Done, pseudonym: $"q-{_tag}", toolStatus: "rejected");
        var fine = Turn(TurnOutcome.Done, pseudonym: $"q-{_tag}");
        var left = Turn(TurnOutcome.Cancelled, pseudonym: $"q-{_tag}");
        // The client left while a tool was running: the tool is "timeout", still not a failure.
        var leftMidTool = Turn(
            TurnOutcome.Cancelled,
            pseudonym: $"q-{_tag}",
            toolStatus: "timeout"
        );
        await SeedAsync(
            failed,
            rejected,
            fine,
            left,
            leftMidTool,
            Turn(TurnOutcome.Error, "assistant.timeout", pseudonym, TimeSpan.FromHours(2)),
            Turn(TurnOutcome.Error, "assistant.timeout", pseudonym, TimeSpan.FromHours(3))
        );
        factory.Clock.Set(_now);
        var admin = await AccountAsync(RoleNames.Admin);

        var queue = (await admin.Client.GetFromJsonAsync<ReviewQueue>(Review))!.Items;
        var ids = queue.Select(i => i.Id).ToList();
        Assert.Contains(failed.Id, ids);
        Assert.Contains(rejected.Id, ids);
        Assert.DoesNotContain(fine.Id, ids);
        Assert.DoesNotContain(left.Id, ids);
        Assert.DoesNotContain(leftMidTool.Id, ids);
        Assert.Equal(["error", "repeated_errors"], queue.Single(i => i.Id == failed.Id).Reasons);
        Assert.Equal(["rejected_tool"], queue.Single(i => i.Id == rejected.Id).Reasons);

        var noCsrf = await admin.Client.PostAsync($"{Review}/{failed.Id}", null);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.Client.PostWithCsrfAsync($"{Review}/{failed.Id}", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await admin.Client.PostWithCsrfAsync($"{Review}/{failed.Id}", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await admin.Client.PostWithCsrfAsync($"{Review}/{Guid.NewGuid()}", null)).StatusCode
        );
        var after = (await admin.Client.GetFromJsonAsync<ReviewQueue>(Review))!.Items;
        Assert.DoesNotContain(after, i => i.Id == failed.Id);
        var stored = await DbAsync(db => db.AssistantTurns.SingleAsync(t => t.Id == failed.Id));
        Assert.Equal((_now, admin.UserId), (stored.ReviewedAt!.Value, stored.ReviewedBy));

        var customer = await AccountAsync(RoleNames.User);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.Client.GetAsync(Review)).StatusCode);
    }

    // ---- retention ----

    [Fact]
    public async Task Retention_removes_only_rows_older_than_the_configured_period()
    {
        var old = Turn(TurnOutcome.Done, ago: TimeSpan.FromDays(31));
        var recent = Turn(TurnOutcome.Done, ago: TimeSpan.FromDays(29));
        var oldEvent = Event(SecurityEventKind.AccessDenied, TimeSpan.FromDays(31));
        var recentEvent = Event(SecurityEventKind.AccessDenied, TimeSpan.FromDays(29));
        await SeedAsync(old, recent, oldEvent, recentEvent);

        await using var scope = factory.Services.CreateAsyncScope();
        var removed = await scope
            .ServiceProvider.GetRequiredService<ObservabilityRetention>()
            .PurgeAsync(_now, CancellationToken.None);

        Assert.True(removed.Turns >= 1 && removed.SecurityEvents >= 1);
        var turns = await DbAsync(db =>
            db.AssistantTurns.Where(t => t.Id == old.Id || t.Id == recent.Id)
                .Select(t => t.Id)
                .ToListAsync()
        );
        var events = await DbAsync(db =>
            db.SecurityEvents.Where(e => e.Id == oldEvent.Id || e.Id == recentEvent.Id)
                .Select(e => e.Id)
                .ToListAsync()
        );
        Assert.Equal([recent.Id], turns);
        Assert.Equal([recentEvent.Id], events);
    }
}

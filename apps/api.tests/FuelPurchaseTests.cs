using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class FuelPurchaseTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Purchases = "/api/cashier/fuel-purchases";
    private const string Card = "/api/me/loyalty-card";
    private readonly List<string> _emails = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
    }

    private sealed record CardResponse(string Code);

    private sealed record PurchaseResponse(Guid PurchaseId, int PointsAwarded);

    private sealed record ProblemCode(string? Code);

    private sealed record Customer(HttpClient Client, string UserId, string Email, string CardCode);

    private string NewEmail(string kind)
    {
        var email = $"loyalty-{kind}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        return email;
    }

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.LoginAsync(email, Password);
        return client;
    }

    private async Task<string> UserIdAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().FindByEmailAsync(email))!.Id;
    }

    private async Task<Customer> CustomerWithCardAsync()
    {
        var email = NewEmail("customer");
        await factory.CreateTestUserAsync(email, Password, RoleNames.User);
        var client = await LoginAsync(email);
        var issued = await PostAsync(client, Card, null, NewKey());
        var code = (await issued.Content.ReadFromJsonAsync<CardResponse>())!.Code;
        return new Customer(client, await UserIdAsync(email), email, code);
    }

    private async Task<(HttpClient Client, string UserId)> CashierAsync(string station = "ST-01", string register = "K1")
    {
        var email = NewEmail("cashier");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await CashierProvisioning.EnsureCashierAsync(scope.ServiceProvider, email, Password, "Test Cashier", station, register);
            await transaction.CommitAsync();
        }
        return (await LoginAsync(email), await UserIdAsync(email));
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private static string NewReceipt() => $"R/{Guid.NewGuid():N}".ToUpperInvariant();

    private static object Purchase(string cardCode, decimal amount = 123.99m, string? receipt = null, decimal liters = 20.5m) =>
        new { cardCode, amount, liters, saleDate = "2026-11-20", receiptNumber = receipt ?? NewReceipt() };

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object? body, string? key, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        if (key is not null)
            request.Headers.Add(Idempotency.HeaderName, key);
        if (csrf)
            request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static async Task<string?> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemCode>())?.Code;

    private static bool IsReplayed(HttpResponseMessage response) =>
        response.Headers.TryGetValues(Idempotency.ReplayedHeaderName, out var values) && values.Contains("true");

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int?> BalanceAsync(string userId) =>
        DbAsync(db => db.LoyaltyAccounts.Where(a => a.UserId == userId).Select(a => (int?)a.Balance).SingleOrDefaultAsync());

    private Task<int> PurchasesOfAsync(string userId) => DbAsync(db => db.FuelPurchases.CountAsync(p => p.UserId == userId));

    [Fact]
    public async Task Purchase_awards_points_at_the_cashier_station_and_records_history()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, cashierId) = await CashierAsync(" st-07 ", "k3");

        var response = await PostAsync(cashier, Purchases, Purchase(customer.CardCode, 123.99m), NewKey());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<PurchaseResponse>();
        Assert.Equal(123, body!.PointsAwarded);
        Assert.Equal(123, await BalanceAsync(customer.UserId));

        var purchase = await DbAsync(db => db.FuelPurchases.AsNoTracking().SingleAsync(p => p.Id == body.PurchaseId));
        Assert.Equal((customer.UserId, cashierId, "ST-07", "K3", 123.99m, 20.5m, 123),
            (purchase.UserId, purchase.RegisteredByUserId, purchase.StationCode, purchase.RegisterCode, purchase.GrossAmount, purchase.Liters, purchase.PointsAwarded));
        Assert.Equal((0m, 123.99m), (purchase.Discount, purchase.PaidAmount));
        var entry = await DbAsync(db => db.PointsLedgerEntries.AsNoTracking().SingleAsync(e => e.SourceId == body.PurchaseId));
        Assert.Equal((customer.UserId, 123, 123), (entry.UserId, entry.Delta, entry.BalanceAfter));
    }

    [Fact]
    public async Task Response_contains_only_the_purchase_id_and_points()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();

        var response = await PostAsync(cashier, Purchases, Purchase(customer.CardCode), NewKey());

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(["pointsAwarded", "purchaseId"], json.RootElement.EnumerateObject().Select(p => p.Name).Order());
    }

    [Fact]
    public async Task Station_from_the_form_is_ignored()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync("ST-01", "K1");
        var body = new
        {
            cardCode = customer.CardCode, amount = 10m, liters = 1m, saleDate = "2026-11-20",
            receiptNumber = NewReceipt(), stationCode = "FORGED", registerCode = "X9",
        };

        var response = await PostAsync(cashier, Purchases, body, NewKey());

        var id = (await response.Content.ReadFromJsonAsync<PurchaseResponse>())!.PurchaseId;
        var purchase = await DbAsync(db => db.FuelPurchases.AsNoTracking().SingleAsync(p => p.Id == id));
        Assert.Equal(("ST-01", "K1"), (purchase.StationCode, purchase.RegisterCode));
    }

    [Fact]
    public async Task Next_purchases_accumulate_and_balance_after_follows_the_ledger()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();

        await PostAsync(cashier, Purchases, Purchase(customer.CardCode, 50m), NewKey());
        await PostAsync(cashier, Purchases, Purchase(customer.CardCode, 70.5m), NewKey());

        Assert.Equal(120, await BalanceAsync(customer.UserId));
        var balances = await DbAsync(db => db.PointsLedgerEntries.Where(e => e.UserId == customer.UserId)
            .OrderBy(e => e.BalanceAfter).Select(e => e.BalanceAfter).ToListAsync());
        Assert.Equal([50, 120], balances);
    }

    [Fact]
    public async Task Purchase_below_one_zloty_is_recorded_without_points_or_history()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();

        var response = await PostAsync(cashier, Purchases, Purchase(customer.CardCode, 0.99m), NewKey());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<PurchaseResponse>())!.PointsAwarded);
        Assert.Equal(1, await PurchasesOfAsync(customer.UserId));
        Assert.Null(await BalanceAsync(customer.UserId));
        Assert.Equal(0, await DbAsync(db => db.PointsLedgerEntries.CountAsync(e => e.UserId == customer.UserId)));
    }

    [Fact]
    public async Task Retry_with_the_same_key_replays_the_purchase_once()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();
        var key = NewKey();
        var body = Purchase(customer.CardCode, 40m);

        var first = await PostAsync(cashier, Purchases, body, key);
        var retry = await PostAsync(cashier, Purchases, body, key);

        Assert.True(IsReplayed(retry));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        Assert.Equal(1, await PurchasesOfAsync(customer.UserId));
        Assert.Equal(40, await BalanceAsync(customer.UserId));
    }

    [Fact]
    public async Task Same_receipt_with_another_key_is_rejected_without_owner_information()
    {
        var customer = await CustomerWithCardAsync();
        var other = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();
        var receipt = NewReceipt();
        await PostAsync(cashier, Purchases, Purchase(customer.CardCode, 40m, receipt), NewKey());

        var duplicate = await PostAsync(cashier, Purchases, Purchase(other.CardCode, 40m, receipt.ToLowerInvariant()), NewKey());

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal(LoyaltyErrorCodes.ReceiptAlreadyRegistered, await ErrorOf(duplicate));
        var text = await duplicate.Content.ReadAsStringAsync();
        Assert.DoesNotContain(customer.UserId, text);
        Assert.DoesNotContain(customer.Email, text);
        Assert.DoesNotContain(customer.CardCode, text);
        Assert.Equal(40, await BalanceAsync(customer.UserId));
        Assert.Null(await BalanceAsync(other.UserId));
    }

    [Fact]
    public async Task Concurrent_purchases_with_one_receipt_and_different_keys_award_points_once()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();
        var receipt = NewReceipt();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => PostAsync(cashier, Purchases, Purchase(customer.CardCode, 30m, receipt), NewKey())));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created),
            r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Equal(30, await BalanceAsync(customer.UserId));
        Assert.Equal(1, await PurchasesOfAsync(customer.UserId));
    }

    [Fact]
    public async Task Concurrent_purchases_for_one_customer_are_all_credited()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();

        var responses = await Task.WhenAll(Enumerable.Range(1, 8)
            .Select(i => PostAsync(cashier, Purchases, Purchase(customer.CardCode, i * 10m), NewKey())));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        Assert.Equal(360, await BalanceAsync(customer.UserId));
        var balances = await DbAsync(db => db.PointsLedgerEntries.Where(e => e.UserId == customer.UserId)
            .OrderBy(e => e.BalanceAfter).Select(e => new { e.Delta, e.BalanceAfter }).ToListAsync());
        var running = 0;
        foreach (var entry in balances)
            Assert.Equal(running += entry.Delta, entry.BalanceAfter);
    }

    [Fact]
    public async Task Typed_card_code_variants_are_normalized()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();
        var typed = string.Join('-', customer.CardCode.Chunk(4).Select(c => new string(c))).ToLowerInvariant();

        var response = await PostAsync(cashier, Purchases, Purchase($"  {typed} ", 10m), NewKey());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(10, await BalanceAsync(customer.UserId));
    }

    [Fact]
    public async Task Unknown_and_revoked_codes_are_rejected_the_same_way()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();
        var oldCode = customer.CardCode;
        await PostAsync(customer.Client, $"{Card}/rotate", null, NewKey());

        var revoked = await PostAsync(cashier, Purchases, Purchase(oldCode), NewKey());
        var unknown = await PostAsync(cashier, Purchases, Purchase(new string('0', 26)), NewKey());

        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Equal(LoyaltyErrorCodes.CardNotFound, await ErrorOf(revoked));
        Assert.Equal(LoyaltyErrorCodes.CardNotFound, await ErrorOf(unknown));
        Assert.Equal(0, await PurchasesOfAsync(customer.UserId));
    }

    [Fact]
    public async Task Invalid_input_is_a_field_error_that_is_not_stored()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, cashierId) = await CashierAsync();
        var key = NewKey();

        var invalid = await PostAsync(cashier, Purchases,
            new { cardCode = "not-a-card", amount = 0m, liters = 1m, saleDate = "2026-11-20", receiptNumber = "R 1" }, key);

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var json = JsonDocument.Parse(await invalid.Content.ReadAsStringAsync());
        Assert.Equal(LoyaltyErrorCodes.ValidationFailed, json.RootElement.GetProperty("code").GetString());
        var errors = json.RootElement.GetProperty("errors");
        Assert.Equal(LoyaltyErrorCodes.InvalidFormat, errors.GetProperty("cardCode")[0].GetProperty("code").GetString());
        Assert.Equal(LoyaltyErrorCodes.AmountOutOfRange, errors.GetProperty("amount")[0].GetProperty("code").GetString());
        Assert.Equal(LoyaltyErrorCodes.InvalidFormat, errors.GetProperty("receiptNumber")[0].GetProperty("code").GetString());

        var corrected = await PostAsync(cashier, Purchases, Purchase(customer.CardCode, 10m), key);
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
        Assert.Equal(1, await DbAsync(db => db.IdempotencyRecords.CountAsync(r => r.UserId == cashierId)));
    }

    [Fact]
    public async Task Numbers_sent_as_strings_are_rejected()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();
        var body = new { cardCode = customer.CardCode, amount = "10", liters = 1m, saleDate = "2026-11-20", receiptNumber = NewReceipt() };

        var response = await PostAsync(cashier, Purchases, body, NewKey());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await PurchasesOfAsync(customer.UserId));
    }

    [Fact]
    public async Task Cashier_without_a_station_cannot_register_purchases()
    {
        var customer = await CustomerWithCardAsync();
        var email = NewEmail("cashier-nostation");
        await factory.CreateTestUserAsync(email, Password, RoleNames.Cashier);
        var cashier = await LoginAsync(email);

        var response = await PostAsync(cashier, Purchases, Purchase(customer.CardCode), NewKey());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(LoyaltyErrorCodes.CashierStationMissing, await ErrorOf(response));
        Assert.Equal(0, await PurchasesOfAsync(customer.UserId));
    }

    [Theory]
    [InlineData(RoleNames.User)]
    [InlineData(RoleNames.Admin)]
    public async Task Only_cashiers_register_purchases(string role)
    {
        var customer = await CustomerWithCardAsync();
        var email = NewEmail("not-cashier");
        await factory.CreateTestUserAsync(email, Password, role);
        var client = await LoginAsync(email);

        var response = await PostAsync(client, Purchases, Purchase(customer.CardCode), NewKey());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, await PurchasesOfAsync(customer.UserId));
    }

    [Fact]
    public async Task Customer_cannot_credit_own_card()
    {
        var customer = await CustomerWithCardAsync();

        var response = await PostAsync(customer.Client, Purchases, Purchase(customer.CardCode), NewKey());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(await BalanceAsync(customer.UserId));
    }

    [Fact]
    public async Task Purchase_requires_csrf_and_an_idempotency_key()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(cashier, Purchases, Purchase(customer.CardCode), NewKey(), csrf: false)).StatusCode);
        Assert.Equal(IdempotencyErrorCodes.KeyRequired, await ErrorOf(await PostAsync(cashier, Purchases, Purchase(customer.CardCode), key: null)));
        Assert.Equal(0, await PurchasesOfAsync(customer.UserId));
    }

    [Fact]
    public async Task Purchases_are_isolated_between_customers()
    {
        var anna = await CustomerWithCardAsync();
        var bartek = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();

        await PostAsync(cashier, Purchases, Purchase(anna.CardCode, 25m), NewKey());

        Assert.Equal(25, await BalanceAsync(anna.UserId));
        Assert.Null(await BalanceAsync(bartek.UserId));
        Assert.Equal(0, await PurchasesOfAsync(bartek.UserId));
    }

    [Fact]
    public async Task Busy_customer_gives_a_retryable_503_whose_retry_after_passes_the_capture_filter()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, cashierId) = await CashierAsync();
        var key = NewKey();
        var body = Purchase(customer.CardCode, 15m);

        await using (await HoldCustomerLockAsync(customer.UserId))
        {
            var busy = await PostAsync(cashier, Purchases, body, key);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
            Assert.Equal(LoyaltyErrorCodes.CustomerBusy, await ErrorOf(busy));
            Assert.Equal("1", busy.Headers.GetValues("Retry-After").Single());
            Assert.Equal(0, await DbAsync(db => db.IdempotencyRecords.CountAsync(r => r.UserId == cashierId)));
        }

        var retry = await PostAsync(cashier, Purchases, body, key);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.False(IsReplayed(retry));
        Assert.Equal(15, await BalanceAsync(customer.UserId));
    }

    [Fact]
    public async Task Race_rotation_committed_first_rejects_the_old_code()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, cashierId) = await CashierAsync();
        var key = NewKey();

        var body = Purchase(customer.CardCode, 50m);
        Task<HttpResponseMessage> purchase;
        await using (var rotation = await HoldCustomerLockAsync(customer.UserId))
        {
            // The purchase has found the (still active) card and now waits for the customer lock.
            purchase = PostAsync(cashier, Purchases, body, key);
            await WaitForLockWaitersAsync(1);

            // The rotation holding the lock revokes the card and commits.
            await rotation.ExecuteAsync(
                """UPDATE "LoyaltyCards" SET "RevokedAt" = now() WHERE "UserId" = @user AND "RevokedAt" IS NULL""",
                customer.UserId);
            await rotation.CommitAsync();
        }

        var response = await purchase;
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(LoyaltyErrorCodes.CardNotFound, await ErrorOf(response));
        Assert.Equal(0, await PurchasesOfAsync(customer.UserId));
        Assert.Null(await BalanceAsync(customer.UserId));
        // A domain outcome: stored, the retry replays the rejection.
        Assert.Equal(1, await DbAsync(db => db.IdempotencyRecords.CountAsync(r => r.UserId == cashierId)));
        var retry = await PostAsync(cashier, Purchases, body, key);
        Assert.True(IsReplayed(retry));
        Assert.Equal(HttpStatusCode.NotFound, retry.StatusCode);
    }

    [Fact]
    public async Task Race_purchase_holding_the_lock_first_is_credited_and_the_rotation_waits()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();
        var oldCode = customer.CardCode;

        Task<HttpResponseMessage> purchase, rotation;
        using (var gate = factory.LockGate.Arm(CustomerLock.Key(customer.UserId)))
        {
            // The purchase gets the customer lock and is paused right after it.
            purchase = PostAsync(cashier, Purchases, Purchase(oldCode, 50m), NewKey());
            await gate.Acquired.WaitAsync(TimeSpan.FromSeconds(5));

            // The rotation starts only now and has to wait for the purchase.
            rotation = PostAsync(customer.Client, $"{Card}/rotate", null, NewKey());
            await WaitForLockWaitersAsync(1);
            Assert.False(rotation.IsCompleted);

            gate.Release();
        }

        var purchased = await purchase;
        var rotated = await rotation;
        Assert.Equal(HttpStatusCode.Created, purchased.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.Equal(50, await BalanceAsync(customer.UserId));
        var newCode = (await rotated.Content.ReadFromJsonAsync<CardResponse>())!.Code;
        Assert.NotEqual(oldCode, newCode);

        var afterRotation = await PostAsync(cashier, Purchases, Purchase(oldCode, 10m), NewKey());
        Assert.Equal(LoyaltyErrorCodes.CardNotFound, await ErrorOf(afterRotation));
        Assert.Equal(HttpStatusCode.Created, (await PostAsync(cashier, Purchases, Purchase(newCode, 10m), NewKey())).StatusCode);
        Assert.Equal(60, await BalanceAsync(customer.UserId));
    }

    [Theory]
    // 22:30 UTC = 23:30 in Warsaw (CET): the station is still on 15 January.
    [InlineData("2027-01-15T22:30:00Z", "2027-01-16", false)]
    [InlineData("2027-01-15T22:30:00Z", "2027-01-15", true)]
    // 23:30 UTC = 00:30 on 16 January in Warsaw, although UTC is still on the 15th.
    [InlineData("2027-01-15T23:30:00Z", "2027-01-16", true)]
    [InlineData("2027-01-15T23:30:00Z", "2027-01-17", false)]
    [InlineData("2027-01-15T23:30:00Z", "2020-01-01", true)]
    public async Task Sale_date_after_today_at_the_station_is_rejected(string utcNow, string saleDate, bool accepted)
    {
        factory.Clock.Set(DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture));
        try
        {
            var customer = await CustomerWithCardAsync();
            var (cashier, _) = await CashierAsync();
            var body = new { cardCode = customer.CardCode, amount = 10m, liters = 1m, saleDate, receiptNumber = NewReceipt() };

            var response = await PostAsync(cashier, Purchases, body, NewKey());

            if (accepted)
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            else
            {
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal(LoyaltyErrorCodes.SaleDateInFuture,
                    json.RootElement.GetProperty("errors").GetProperty("saleDate")[0].GetProperty("code").GetString());
                Assert.Equal(0, await PurchasesOfAsync(customer.UserId));
            }
        }
        finally
        {
            factory.Clock.Reset();
        }
    }

    [Fact]
    public async Task Card_code_from_the_purchase_request_never_reaches_the_logs()
    {
        var customer = await CustomerWithCardAsync();
        var (cashier, _) = await CashierAsync();
        var before = factory.Logs.Messages.Count;

        await PostAsync(cashier, Purchases, Purchase(customer.CardCode, 10m), NewKey());
        await PostAsync(cashier, Purchases, Purchase(new string('0', 26)), NewKey());

        var messages = factory.Logs.Messages;
        Assert.True(messages.Count > before);
        Assert.Contains(messages, m => m.Contains("FuelPurchases"));
        Assert.DoesNotContain(messages, m => m.Contains(customer.CardCode) || m.Contains(new string('0', 26)));
    }

    private async Task WaitForLockWaitersAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var waiting = await DbAsync(db => db.Database
                .SqlQuery<int>($"""SELECT count(*)::int AS "Value" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted""")
                .SingleAsync());
            if (waiting >= count)
                return;
            await Task.Delay(10);
        }
        throw new TimeoutException($"Expected {count} request(s) waiting for the customer lock.");
    }

    private async Task<LockHolder> HoldCustomerLockAsync(string userId)
    {
        var connection = new NpgsqlConnection(factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection"));
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        var holder = new LockHolder(connection, transaction);
        await holder.ExecuteAsync("SELECT pg_advisory_xact_lock(hashtextextended(@user, 0))", CustomerLock.Key(userId));
        return holder;
    }

    private sealed class LockHolder(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        private bool _committed;

        public async Task ExecuteAsync(string sql, string user)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            command.Parameters.AddWithValue("user", user);
            await command.ExecuteNonQueryAsync();
        }

        public async Task CommitAsync()
        {
            await transaction.CommitAsync();
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed)
                await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }
}

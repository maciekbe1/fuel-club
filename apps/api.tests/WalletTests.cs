using System.Net;
using System.Net.Http.Json;
using System.Text;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using FuelClub.Api.Modules.Loyalty.Wallet;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class WalletTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Wallet = "/api/me/wallet";
    private const string Transactions = "/api/me/transactions";
    private readonly List<string> _emails = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
    }

    private sealed record CardResponse(string Code);

    private sealed record PurchaseResponse(Guid PurchaseId, int PointsAwarded);

    private sealed record ProblemBody(string? Code, Dictionary<string, FieldError[]>? Errors);

    private sealed record FieldError(string Field, string Code);

    private sealed record Customer(HttpClient Client, string UserId, string CardCode);

    private async Task<(HttpClient Client, string UserId)> AccountAsync(params string[] roles)
    {
        var email = $"loyalty-wallet-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, roles[0]);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByEmailAsync(email))!;
        foreach (var role in roles.Skip(1))
            await users.AddToRoleAsync(user, role);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.LoginAsync(email, Password);
        return (client, user.Id);
    }

    private async Task<Customer> CustomerWithCardAsync()
    {
        var (client, userId) = await AccountAsync(RoleNames.User);
        var issued = await PostAsync(client, "/api/me/loyalty-card", null);
        return new Customer(client, userId, (await issued.Content.ReadFromJsonAsync<CardResponse>())!.Code);
    }

    private async Task<HttpClient> CashierAsync()
    {
        var email = $"loyalty-wallet-cashier-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await CashierProvisioning.EnsureCashierAsync(scope.ServiceProvider, email, Password, "Cashier", "ST-09", "K1");
            await transaction.CommitAsync();
        }
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.LoginAsync(email, Password);
        return client;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object? body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = body is null ? null : JsonContent.Create(body) };
        request.Headers.Add(Idempotency.HeaderName, Guid.NewGuid().ToString());
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> BuyAsync(HttpClient cashier, string cardCode, decimal amount) =>
        PostAsync(cashier, "/api/cashier/fuel-purchases", new
        {
            cardCode, amount, liters = 12.345m, saleDate = "2026-11-20", receiptNumber = $"W/{Guid.NewGuid():N}".ToUpperInvariant(),
        });

    private static async Task<TransactionsResponse> PageAsync(HttpClient client, string query = "") =>
        (await client.GetFromJsonAsync<TransactionsResponse>(Transactions + query))!;

    private async Task<List<TransactionItem>> AllAsync(HttpClient client, int limit)
    {
        var all = new List<TransactionItem>();
        string? cursor = null;
        do
        {
            var page = await PageAsync(client, $"?limit={limit}" + (cursor is null ? "" : $"&cursor={cursor}"));
            Assert.True(page.Items.Count <= limit);
            all.AddRange(page.Items);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return all;
    }

    /// <summary>Ledger rows written directly (no purchase behind them); Sequence comes from the database.</summary>
    private async Task<List<Guid>> InsertEntriesAsync(string userId, int count, Guid? sourceId = null)
    {
        var ids = new List<Guid>();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        for (var i = 1; i <= count; i++)
        {
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "PointsLedgerEntries" ("Id", "UserId", "Delta", "Type", "SourceId", "BalanceAfter")
                VALUES ({id}, {userId}, 1, 'FuelPurchase', {sourceId ?? Guid.NewGuid()}, {i})
                """);
            ids.Add(id);
        }
        return ids;
    }

    private async Task<long> SequenceOfAsync(Guid entryId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().PointsLedgerEntries
            .Where(e => e.Id == entryId).Select(e => e.Sequence).SingleAsync();
    }

    [Fact]
    public async Task Wallet_is_zero_before_any_credit()
    {
        var (client, _) = await AccountAsync(RoleNames.User);

        Assert.Equal(0, (await client.GetFromJsonAsync<WalletResponse>(Wallet))!.Balance);
        Assert.Empty((await PageAsync(client)).Items);
    }

    [Fact]
    public async Task Balance_matches_the_history_and_zero_point_purchases_are_not_listed()
    {
        var customer = await CustomerWithCardAsync();
        var cashier = await CashierAsync();
        var first = await (await BuyAsync(cashier, customer.CardCode, 40.5m)).Content.ReadFromJsonAsync<PurchaseResponse>();
        var zero = await (await BuyAsync(cashier, customer.CardCode, 0.99m)).Content.ReadFromJsonAsync<PurchaseResponse>();
        var second = await (await BuyAsync(cashier, customer.CardCode, 12m)).Content.ReadFromJsonAsync<PurchaseResponse>();

        var balance = (await customer.Client.GetFromJsonAsync<WalletResponse>(Wallet))!.Balance;
        var items = (await PageAsync(customer.Client)).Items;

        Assert.Equal(52, balance);
        Assert.Equal(balance, items.Sum(item => item.Delta));
        Assert.Equal(balance, items[0].BalanceAfter);
        Assert.Equal([second!.PurchaseId, first!.PurchaseId], items.Select(item => item.FuelPurchase!.PurchaseId));
        Assert.DoesNotContain(items, item => item.FuelPurchase?.PurchaseId == zero!.PurchaseId);
        Assert.Equal(0, zero!.PointsAwarded);
    }

    [Fact]
    public async Task History_item_describes_the_linked_purchase()
    {
        var customer = await CustomerWithCardAsync();
        var cashier = await CashierAsync();
        var purchase = await (await BuyAsync(cashier, customer.CardCode, 40.5m)).Content.ReadFromJsonAsync<PurchaseResponse>();

        var item = Assert.Single((await PageAsync(customer.Client)).Items);

        Assert.Equal(("FuelPurchase", 40, 40), (item.Type, item.Delta, item.BalanceAfter));
        Assert.Equal(new TransactionFuelPurchase(purchase!.PurchaseId, "ST-09", new DateOnly(2026, 11, 20), 40.5m, 12.345m), item.FuelPurchase);
    }

    [Theory]
    [InlineData(45, 20)]
    [InlineData(10, 5)]
    [InlineData(3, 100)]
    public async Task Pages_cover_the_history_without_duplicates_or_gaps(int entries, int limit)
    {
        var (client, userId) = await AccountAsync(RoleNames.User);
        var inserted = await InsertEntriesAsync(userId, entries);

        var all = await AllAsync(client, limit);

        Assert.Equal(inserted.AsEnumerable().Reverse(), all.Select(item => item.Id));
    }

    [Fact]
    public async Task Last_full_page_has_no_next_cursor()
    {
        var (client, userId) = await AccountAsync(RoleNames.User);
        await InsertEntriesAsync(userId, 10);

        var first = await PageAsync(client, "?limit=5");
        var second = await PageAsync(client, $"?limit=5&cursor={first.NextCursor}");

        Assert.NotNull(first.NextCursor);
        Assert.Equal(5, second.Items.Count);
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task Default_page_size_is_twenty()
    {
        var (client, userId) = await AccountAsync(RoleNames.User);
        await InsertEntriesAsync(userId, 21);

        var page = await PageAsync(client);

        Assert.Equal(WalletEndpoints.DefaultLimit, page.Items.Count);
        Assert.NotNull(page.NextCursor);
    }

    [Fact]
    public async Task Entries_added_while_paging_appear_after_reloading_the_first_page()
    {
        var (client, userId) = await AccountAsync(RoleNames.User);
        await InsertEntriesAsync(userId, 4);
        var first = await PageAsync(client, "?limit=2");

        var added = (await InsertEntriesAsync(userId, 1)).Single();
        var second = await PageAsync(client, $"?limit=2&cursor={first.NextCursor}");
        var reloaded = await PageAsync(client, "?limit=2");

        Assert.DoesNotContain(second.Items, item => item.Id == added);
        Assert.Equal(added, reloaded.Items[0].Id);
    }

    [Theory]
    [InlineData("?limit=0", "limit", "loyalty.limit_out_of_range")]
    [InlineData("?limit=101", "limit", "loyalty.limit_out_of_range")]
    [InlineData("?cursor=not-a-cursor", "cursor", "loyalty.invalid_cursor")]
    [InlineData("?cursor=djE6LTU", "cursor", "loyalty.invalid_cursor")]
    [InlineData("?cursor=djE6eA", "cursor", "loyalty.invalid_cursor")]
    [InlineData("?cursor=MTI", "cursor", "loyalty.invalid_cursor")]
    public async Task Invalid_paging_parameters_are_field_errors(string query, string field, string code)
    {
        var (client, _) = await AccountAsync(RoleNames.User);

        var response = await client.GetAsync(Transactions + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>();
        Assert.Equal(LoyaltyErrorCodes.ValidationFailed, problem!.Code);
        Assert.Equal(code, problem.Errors![field][0].Code);
    }

    [Fact]
    public async Task History_is_ordered_by_write_order_even_when_transactions_started_in_reverse()
    {
        var customer = await CustomerWithCardAsync();
        var cashier = await CashierAsync();

        // A starts its transaction first and is paused before it can take the customer lock.
        Task<HttpResponseMessage> purchaseA;
        using (var gate = factory.LockGate.ArmWhen(command =>
                   command.CommandText.StartsWith("SET LOCAL lock_timeout = ", StringComparison.Ordinal)))
        {
            purchaseA = BuyAsync(cashier, customer.CardCode, 10m);
            await gate.Acquired.WaitAsync(TimeSpan.FromSeconds(5));
            await Delay();

            // B starts later, takes the lock and commits while A waits.
            Assert.Equal(HttpStatusCode.Created, (await BuyAsync(cashier, customer.CardCode, 20m)).StatusCode);
            gate.Release();
        }
        Assert.Equal(HttpStatusCode.Created, (await purchaseA).StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<AppDbContext>().PointsLedgerEntries
            .Where(e => e.UserId == customer.UserId).OrderBy(e => e.Sequence).ToListAsync();
        var (b, a) = (entries[0], entries[1]);
        Assert.Equal((20, 10), (b.Delta, a.Delta));
        // The scenario really happened: A's transaction started earlier but A was written later.
        Assert.True(a.CreatedAt < b.CreatedAt, "A must have the earlier transaction start.");
        Assert.Equal((20, 30), (b.BalanceAfter, a.BalanceAfter));

        var items = (await PageAsync(customer.Client)).Items;
        Assert.Equal([a.Id, b.Id], items.Select(item => item.Id));
        Assert.Equal([30, 20], items.Select(item => item.BalanceAfter));
    }

    private static Task Delay() => Task.Delay(50);

    [Fact]
    public async Task Customers_see_only_their_own_history_even_with_someone_elses_cursor()
    {
        var (anna, annaId) = await AccountAsync(RoleNames.User);
        var (bartek, bartekId) = await AccountAsync(RoleNames.User);
        var bartekEntries = await InsertEntriesAsync(bartekId, 2);
        var annaEntries = await InsertEntriesAsync(annaId, 3);
        var annaCursor = (await PageAsync(anna, "?limit=1")).NextCursor;

        var bartekWithAnnaCursor = await PageAsync(bartek, $"?limit=100&cursor={annaCursor}");
        var annaAll = await AllAsync(anna, 100);

        Assert.All(bartekWithAnnaCursor.Items, item => Assert.Contains(item.Id, bartekEntries));
        Assert.Equal(annaEntries.AsEnumerable().Reverse(), annaAll.Select(item => item.Id));
        Assert.Equal(0, (await bartek.GetFromJsonAsync<WalletResponse>(Wallet))!.Balance);
    }

    [Fact]
    public async Task Linked_purchase_is_shown_only_when_it_belongs_to_the_same_customer()
    {
        var anna = await CustomerWithCardAsync();
        var (bartek, bartekId) = await AccountAsync(RoleNames.User);
        var cashier = await CashierAsync();
        // UNIQUE (Type, SourceId) already stops a second entry for a credited purchase; a 0-point
        // purchase has no entry, so only it can be referenced by someone else's row.
        var annaPurchase = await (await BuyAsync(cashier, anna.CardCode, 0.99m)).Content.ReadFromJsonAsync<PurchaseResponse>();
        await InsertEntriesAsync(bartekId, 1, sourceId: annaPurchase!.PurchaseId);

        var item = Assert.Single((await PageAsync(bartek)).Items);

        Assert.Null(item.FuelPurchase);
    }

    [Theory]
    [InlineData(RoleNames.Cashier)]
    [InlineData(RoleNames.Admin)]
    public async Task Staff_roles_alone_do_not_give_access(string role)
    {
        var (client, _) = await AccountAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Wallet)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Transactions)).StatusCode);
    }

    [Theory]
    [InlineData(RoleNames.Cashier)]
    [InlineData(RoleNames.Admin)]
    public async Task Customer_with_an_extra_staff_role_reads_own_data(string extraRole)
    {
        var (client, userId) = await AccountAsync(RoleNames.User, extraRole);
        await InsertEntriesAsync(userId, 2);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Wallet)).StatusCode);
        Assert.Equal(2, (await PageAsync(client)).Items.Count);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Wallet)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Transactions)).StatusCode);
    }

    [Fact]
    public void Cursor_is_opaque_base64url()
    {
        var cursor = WalletEndpoints.EncodeCursor(42);

        Assert.Equal("v1:42", Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor)));
        Assert.DoesNotContain('=', cursor);
    }
}

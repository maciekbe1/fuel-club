using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FuelClub.Api.Tests;

public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _connectionString;

    public ApiFactory()
    {
        _connectionString =
            Environment.GetEnvironmentVariable("FUELCLUB_TEST_DATABASE")
            ?? throw new InvalidOperationException(
                "Set FUELCLUB_TEST_DATABASE to a dedicated PostgreSQL database (fuelclub_test)."
            );

        var database = new NpgsqlConnectionStringBuilder(_connectionString).Database;
        if (!string.Equals(database, "fuelclub_test", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Integration tests require database 'fuelclub_test'; got '{database}'."
            );
    }

    public TestFaults Faults { get; } = new();

    public CapturedLogs Logs { get; } = new();

    public MutableTimeProvider Clock { get; } = new();

    public CustomerLockGate LockGate { get; } = new();

    /// <summary>
    /// A host with the production session check interval. With the interval at zero every request
    /// renews the auth cookie, and the cookie handler then adds no-store to every response, which
    /// would hide a missing Cache-Control in caching tests.
    /// </summary>
    public WebApplicationFactory<Program> WithoutPerRequestSessionRenewal() =>
        WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.Configure<SecurityStampValidatorOptions>(options =>
                    options.ValidationInterval = TimeSpan.FromMinutes(30)
                )
            )
        );

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder
            .ConfigureLogging(logging =>
            {
                // Everything, including EF Core SQL at Trace, goes to the capture; console keeps its levels.
                logging.AddProvider(Logs);
                logging.AddFilter<CapturedLogs>(null, LogLevel.Trace);
            })
            .ConfigureServices(services =>
            {
                services.AddSingleton(Faults);
                services.AddSingleton<TimeProvider>(Clock);
                services.AddSingleton(LockGate);
                services.ConfigureDbContext<AppDbContext>(options =>
                    options.AddInterceptors(LockGate)
                );
                services.AddSingleton<IAdditionalEndpoints, IdempotencyTestEndpoints>();
                // Re-read roles from the database on every request, so tests can revoke a role mid-session.
                services.Configure<SecurityStampValidatorOptions>(options =>
                    options.ValidationInterval = TimeSpan.Zero
                );
            })
            .ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:DefaultConnection"] = _connectionString,
                            // Tests create their own accounts; ignore demo data and bootstrap staff from user-secrets.
                            ["DemoAccounts:Enabled"] = "false",
                            ["BootstrapAdmin:Email"] = "",
                            ["BootstrapAdmin:Password"] = "",
                            ["BootstrapCashier:Email"] = "",
                            ["BootstrapCashier:Password"] = "",
                            ["BootstrapCashier:StationCode"] = "",
                            ["BootstrapCashier:RegisterCode"] = "",
                            ["Idempotency:LockTimeout"] = "00:00:01",
                            ["Loyalty:CustomerLockTimeout"] = "00:00:01",
                            ["Rewards:OfferLockTimeout"] = "00:00:01",
                            ["Rewards:CouponLockTimeout"] = "00:00:01",
                            ["Rewards:PreviewPermitLimit"] = "10",
                            // All tests log in from one address; RateLimitTests lower these per host.
                            ["RateLimits:AuthPermitLimit"] = "100000",
                            // Tests run retention and alerts themselves.
                            ["Observability:WorkerEnabled"] = "false",
                            ["RateLimits:PurchasePermitLimit"] = "100000",
                        }
                    )
            );

    public async Task InitializeAsync()
    {
        // The API does not migrate on startup, so the test database is migrated before the host starts.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
        await using (var db = new AppDbContext(options))
            await db.Database.MigrateAsync();

        await DeleteTestUsersAsync();
    }

    public async Task DeleteTestUserAsync(string email)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
            return;
        await DeleteLoyaltyDataAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            [user.Id]
        );
        await userManager.DeleteAsync(user);
    }

    // Loyalty rows reference users with ON DELETE RESTRICT, so test cleanup removes them first.
    private static async Task DeleteLoyaltyDataAsync(
        AppDbContext db,
        IReadOnlyCollection<string> userIds
    )
    {
        await db
            .IdempotencyRecords.Where(record => userIds.Contains(record.UserId))
            .ExecuteDeleteAsync();
        await db
            .PointsLedgerEntries.Where(entry => userIds.Contains(entry.UserId))
            .ExecuteDeleteAsync();
        await db
            .Coupons.Where(coupon =>
                userIds.Contains(coupon.UserId)
                || db.FuelPurchases.Any(purchase =>
                    purchase.Id == coupon.RedeemedPurchaseId
                    && (
                        userIds.Contains(purchase.UserId)
                        || userIds.Contains(purchase.RegisteredByUserId)
                    )
                )
            )
            .ExecuteDeleteAsync();
        await db
            .FuelPurchases.Where(purchase =>
                userIds.Contains(purchase.UserId) || userIds.Contains(purchase.RegisteredByUserId)
            )
            .ExecuteDeleteAsync();
        await db
            .LoyaltyAccounts.Where(account => userIds.Contains(account.UserId))
            .ExecuteDeleteAsync();
        await db
            .CashierStations.Where(station => userIds.Contains(station.UserId))
            .ExecuteDeleteAsync();
        await db.LoyaltyCards.Where(card => userIds.Contains(card.UserId)).ExecuteDeleteAsync();
    }

    public async Task CreateTestUserAsync(string email, string password, string role)
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser
        {
            Email = email,
            UserName = email,
            DisplayName = "Integration Test",
        };
        var created = await userManager.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException(
                string.Join(", ", created.Errors.Select(error => error.Code))
            );
        var assigned = await userManager.AddToRoleAsync(user, role);
        if (!assigned.Succeeded)
            throw new InvalidOperationException(
                string.Join(", ", assigned.Errors.Select(error => error.Code))
            );
    }

    private async Task DeleteTestUsersAsync()
    {
        using var scope = Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var staleUsers = await userManager
            .Users.Where(user =>
                user.Email != null
                && (
                    user.Email.StartsWith("integration-")
                    || user.Email.StartsWith("logout-check-")
                    || user.Email.StartsWith("role-check-")
                    || user.Email.StartsWith("loyalty-")
                    || user.Email.StartsWith("cashier-")
                    || user.Email.StartsWith("idempotency-")
                    || user.Email.StartsWith("card-")
                    || user.Email.StartsWith("assistant-")
                )
            )
            .ToListAsync();
        await DeleteLoyaltyDataAsync(
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            staleUsers.Select(user => user.Id).ToList()
        );
        foreach (var user in staleUsers)
            await userManager.DeleteAsync(user);
    }

    public new Task DisposeAsync() => base.DisposeAsync().AsTask();
}

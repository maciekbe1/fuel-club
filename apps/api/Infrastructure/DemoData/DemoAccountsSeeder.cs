using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Accounts.Infrastructure;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Infrastructure.DemoData;

/// <summary>
/// Demo accounts for local development. Creates accounts only in the Development environment and only
/// when DemoAccounts:Enabled is true (opt-in via user-secrets, together with DemoAccounts:Password).
/// It never deletes or disables accounts: demo accounts already present in a database stay usable
/// in any environment. Existing accounts keep their password; changing the setting does not reset it.
/// </summary>
public sealed class DemoAccountsSeeder(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<DemoAccountsSeeder> logger
) : IHostedService
{
    public const string EnabledKey = "DemoAccounts:Enabled";
    public const string PasswordKey = "DemoAccounts:Password";

    public static readonly (string Email, string DisplayName, string Role)[] Accounts =
    [
        ("anna@demo.fuelclub.test", "Anna", RoleNames.User),
        ("bartek@demo.fuelclub.test", "Bartek", RoleNames.User),
        ("kasjer@demo.fuelclub.test", "Kasjer demo", RoleNames.Cashier),
        ("admin@demo.fuelclub.test", "Administrator demo", RoleNames.Admin),
    ];

    /// <summary>Product rewards (ADR 0013): no stock, no expiry. Created once by name, never changed.</summary>
    public static readonly (string Name, string Description, int PointsCost)[] Rewards =
    [
        ("Kawa", "Dowolna kawa z ekspresu na stacji.", 150),
        ("Hot dog", "Hot dog z dowolnym dodatkiem.", 250),
        ("Myjnia", "Program podstawowy myjni automatycznej.", 600),
    ];

    public const string CashierStationCode = "DEMO-01";
    public const string CashierRegisterCode = "K1";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue<bool>(EnabledKey))
            return;

        var password = configuration[PasswordKey];
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                $"{PasswordKey} must be configured when {EnabledKey} is true."
            );

        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        foreach (var (email, displayName, role) in Accounts)
        {
            if (role == RoleNames.Cashier)
                await CashierProvisioning.EnsureCashierAsync(
                    scope.ServiceProvider,
                    email,
                    password,
                    displayName,
                    CashierStationCode,
                    CashierRegisterCode
                );
            else
                await AccountProvisioning.EnsureAccountAsync(
                    users,
                    email,
                    password,
                    displayName,
                    role
                );
        }

        foreach (var (name, description, cost) in Rewards)
            if (!await db.RewardOffers.AnyAsync(offer => offer.Name == name, cancellationToken))
                db.RewardOffers.Add(
                    new RewardOffer
                    {
                        Id = Guid.CreateVersion7(),
                        Name = name,
                        Description = description,
                        PointsCost = cost,
                        Kind = RewardKind.Product,
                        IsActive = true,
                        Version = 1,
                    }
                );
        await db.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation(
            "Demo accounts ({Count}) and rewards ({Rewards}) ensured.",
            Accounts.Length,
            Rewards.Length
        );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

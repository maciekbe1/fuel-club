using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Accounts.Infrastructure;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using Microsoft.AspNetCore.Identity;

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

        await transaction.CommitAsync(cancellationToken);
        logger.LogInformation("Demo accounts ensured ({Count}).", Accounts.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

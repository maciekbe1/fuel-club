using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Accounts.Infrastructure;
using FuelClub.Api.Modules.Loyalty.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Loyalty.Infrastructure;

public static class CashierProvisioning
{
    /// <summary>
    /// Ensures a cashier account with the given station. Refuses to promote an existing account
    /// that lacks the Cashier role. Callers own the surrounding transaction.
    /// </summary>
    public static async Task<ApplicationUser> EnsureCashierAsync(
        IServiceProvider services,
        string email,
        string password,
        string displayName,
        string stationCode,
        string registerCode
    )
    {
        var station = FuelPurchaseRules.NormalizeCode(stationCode);
        var register = FuelPurchaseRules.NormalizeCode(registerCode);
        var errors = FuelPurchaseRules.ValidateStation(station, register);
        if (errors.Count > 0)
            throw new InvalidOperationException(
                $"Invalid cashier station: {string.Join(", ", errors.Select(error => $"{error.Field}={error.Code}"))}"
            );

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<AppDbContext>();

        var user = await AccountProvisioning.EnsureAccountAsync(
            users,
            email,
            password,
            displayName,
            RoleNames.Cashier
        );

        var assignment = await db.CashierStations.SingleOrDefaultAsync(row =>
            row.UserId == user.Id
        );
        if (assignment is null)
            db.CashierStations.Add(
                new CashierStation
                {
                    UserId = user.Id,
                    StationCode = station,
                    RegisterCode = register,
                }
            );
        else
        {
            assignment.StationCode = station;
            assignment.RegisterCode = register;
        }

        await db.SaveChangesAsync();
        return user;
    }
}

/// <summary>Creates the cashier configured in BootstrapCashier:* at startup. All four keys or none.</summary>
public sealed class CashierAccountProvisioner(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration
) : IHostedService
{
    public const string Section = "BootstrapCashier";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string?[] values =
        [
            configuration[$"{Section}:Email"],
            configuration[$"{Section}:Password"],
            configuration[$"{Section}:StationCode"],
            configuration[$"{Section}:RegisterCode"],
        ];
        if (values.All(string.IsNullOrWhiteSpace))
            return;
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new InvalidOperationException(
                $"{Section}:Email, Password, StationCode and RegisterCode must all be configured, or none of them."
            );

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await CashierProvisioning.EnsureCashierAsync(
            scope.ServiceProvider,
            values[0]!,
            values[1]!,
            "Kasjer",
            values[2]!,
            values[3]!
        );

        await transaction.CommitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

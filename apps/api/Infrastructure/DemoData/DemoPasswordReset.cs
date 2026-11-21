using FuelClub.Api.Modules.Accounts;
using Microsoft.AspNetCore.Identity;

namespace FuelClub.Api.Infrastructure.DemoData;

/// <summary>
/// One-off command: <c>dotnet run --project apps/api -- reset-demo-passwords</c>.
/// Sets the password of existing demo accounts to DemoAccounts:Password through Identity, keeping
/// their IDs, roles and cashier station. Development only. Never creates or deletes accounts,
/// never runs on startup and never writes the password to logs.
/// </summary>
public static class DemoPasswordReset
{
    public const string Command = "reset-demo-passwords";

    public static async Task<int> RunAsync(IServiceProvider services, IHostEnvironment environment, IConfiguration configuration)
    {
        using var scope = services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoPasswordReset));

        if (!environment.IsDevelopment())
        {
            logger.LogError("{Command} is available only in the Development environment.", Command);
            return 1;
        }

        var password = configuration[DemoAccountsSeeder.PasswordKey];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogError("{Key} is not configured (set it in user-secrets).", DemoAccountsSeeder.PasswordKey);
            return 1;
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();

        var reset = 0;
        foreach (var (email, _, _) in DemoAccountsSeeder.Accounts)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                logger.LogWarning("Demo account {Email} does not exist; skipped (not created).", email);
                continue;
            }

            var token = await users.GeneratePasswordResetTokenAsync(user);
            var result = await users.ResetPasswordAsync(user, token, password);
            if (!result.Succeeded)
            {
                logger.LogError(
                    "Password reset failed for {Email}: {Codes}. No account was changed.",
                    email,
                    string.Join(", ", result.Errors.Select(error => error.Code)));
                return 1;
            }

            reset++;
        }

        await transaction.CommitAsync();
        logger.LogInformation("Demo passwords reset: {Count} of {Total}.", reset, DemoAccountsSeeder.Accounts.Length);
        return 0;
    }
}

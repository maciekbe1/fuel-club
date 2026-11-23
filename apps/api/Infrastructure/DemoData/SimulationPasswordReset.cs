using System.Security.Cryptography;
using System.Text;
using FuelClub.Api.Modules.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Infrastructure.DemoData;

/// <summary>
/// One-off command after rotating the simulator secret:
/// <c>dotnet run --project apps/api -- reset-simulation-passwords</c> with <c>Simulation:Secret</c>
/// (the new SIM_SECRET) in the environment. Sets the password of every existing simulator account
/// (<c>@sim.fuelclub.test</c>) to the one the simulator derives from that secret, through Identity
/// (new security stamp). Development only, all or nothing, never logs a password or the secret.
/// </summary>
public static class SimulationPasswordReset
{
    public const string Command = "reset-simulation-passwords";
    public const string SecretKey = "Simulation:Secret";
    public const string EmailDomain = "@sim.fuelclub.test";

    /// <summary>The simulator's derivation (Population.PasswordFor); a test keeps them equal.</summary>
    public static string PasswordFor(string secret, string email) =>
        Convert
            .ToBase64String(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(email))
            )[..20]
            .Replace('+', 'x')
            .Replace('/', 'y') + "aA1!";

    public static async Task<int> RunAsync(
        IServiceProvider services,
        IHostEnvironment environment,
        IConfiguration configuration
    )
    {
        using var scope = services.CreateScope();
        var logger = scope
            .ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(SimulationPasswordReset));

        if (!environment.IsDevelopment())
        {
            logger.LogError("{Command} is available only in the Development environment.", Command);
            return 1;
        }
        var secret = configuration[SecretKey];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 16)
        {
            logger.LogError("{Key} is not configured (at least 16 characters).", SecretKey);
            return 1;
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var accounts = await db
            .Users.Where(u => u.NormalizedEmail!.EndsWith(EmailDomain.ToUpperInvariant()))
            .OrderBy(u => u.NormalizedEmail)
            .ToListAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var user in accounts)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var result = await users.ResetPasswordAsync(
                user,
                token,
                PasswordFor(secret, user.Email!)
            );
            if (!result.Succeeded)
            {
                logger.LogError(
                    "Password reset failed for {Email}: {Codes}. No account was changed.",
                    user.Email,
                    string.Join(", ", result.Errors.Select(error => error.Code))
                );
                return 1;
            }
        }
        await transaction.CommitAsync();
        logger.LogInformation("Simulation passwords reset: {Count} accounts.", accounts.Count);
        return 0;
    }
}

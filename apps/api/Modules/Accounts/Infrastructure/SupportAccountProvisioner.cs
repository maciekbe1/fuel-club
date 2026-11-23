using FuelClub.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace FuelClub.Api.Modules.Accounts.Infrastructure;

/// <summary>
/// Creates the support agent account from BootstrapSupport:Email / Password (secrets: environment or
/// user-secrets), on any environment (ADR 0012). Only a new account: an existing account with that
/// e-mail never gets the Support role - a warning is logged and the account stays as it is.
/// </summary>
public sealed class SupportAccountProvisioner(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<SupportAccountProvisioner> logger
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = configuration["BootstrapSupport:Email"];
        var password = configuration["BootstrapSupport:Password"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
            return;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "Both BootstrapSupport:Email and BootstrapSupport:Password must be configured."
            );

        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var existing = await users.FindByEmailAsync(email);
        if (existing is not null)
        {
            // The e-mail is not logged: the account is identified by its id.
            if (!await users.IsInRoleAsync(existing, RoleNames.Support))
                logger.LogWarning(
                    "BootstrapSupport skipped: account {UserId} already exists without the Support role and was not changed",
                    existing.Id
                );
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await AccountProvisioning.EnsureAccountAsync(
            users,
            email,
            password,
            "Konsultant",
            RoleNames.Support
        );
        await transaction.CommitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

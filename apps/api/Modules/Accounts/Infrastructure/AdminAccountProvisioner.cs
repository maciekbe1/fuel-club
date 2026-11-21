using FuelClub.Api.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace FuelClub.Api.Modules.Accounts.Infrastructure;

public sealed class AdminAccountProvisioner(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration
) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = configuration["BootstrapAdmin:Email"];
        var password = configuration["BootstrapAdmin:Password"];
        if (string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(password))
            return;
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException(
                "Both BootstrapAdmin:Email and BootstrapAdmin:Password must be configured."
            );

        using var scope = scopeFactory.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        await AccountProvisioning.EnsureAccountAsync(users, email, password, "Administrator", RoleNames.Admin);

        await transaction.CommitAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

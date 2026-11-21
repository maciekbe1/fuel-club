using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Infrastructure;

/// <summary>
/// Fails fast when migrations defined in code are missing from __EFMigrationsHistory.
/// Checks migration history only, not the actual schema: manual schema changes are not detected.
/// Migrations are applied as a separate step (dotnet ef database update / migrations bundle).
/// </summary>
public sealed class DatabaseSchemaVerifier(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count > 0)
            throw new InvalidOperationException(
                $"Database schema is out of date. Pending migrations: {string.Join(", ", pending)}. "
                    + "Apply them with 'dotnet ef database update --project apps/api' before starting the API."
            );
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

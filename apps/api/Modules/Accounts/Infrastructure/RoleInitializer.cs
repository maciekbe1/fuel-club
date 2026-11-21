using Microsoft.AspNetCore.Identity;

namespace FuelClub.Api.Modules.Accounts.Infrastructure;

public sealed class RoleInitializer(IServiceScopeFactory scopeFactory) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

        foreach (var roleName in RoleNames.All)
        {
            if (await roleManager.RoleExistsAsync(roleName))
                continue;

            var result = await roleManager.CreateAsync(new IdentityRole(roleName));
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Could not initialize role '{roleName}': {string.Join(", ", result.Errors.Select(error => error.Code))}"
                );
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

using Microsoft.AspNetCore.Identity;

namespace FuelClub.Api.Modules.Accounts.Infrastructure;

/// <summary>
/// Creates configured accounts (bootstrap staff, demo data). Never promotes an existing account:
/// if the e-mail already exists without the expected role, provisioning is refused.
/// Callers own the surrounding transaction.
/// </summary>
public static class AccountProvisioning
{
    public static async Task<ApplicationUser> EnsureAccountAsync(
        UserManager<ApplicationUser> users,
        string email,
        string password,
        string displayName,
        string role
    )
    {
        var user = await users.FindByEmailAsync(email);
        if (user is not null)
        {
            if (!await users.IsInRoleAsync(user, role))
                throw new InvalidOperationException(
                    $"Account provisioning refused: account '{email}' already exists without the {role} role."
                );
            return user;
        }

        user = new ApplicationUser { Email = email, UserName = email, DisplayName = displayName };
        var created = await users.CreateAsync(user, password);
        if (!created.Succeeded)
            throw new InvalidOperationException(
                $"Could not provision account '{email}': {string.Join(", ", created.Errors.Select(error => error.Code))}"
            );

        var assigned = await users.AddToRoleAsync(user, role);
        if (!assigned.Succeeded)
            throw new InvalidOperationException(
                $"Could not assign role {role}: {string.Join(", ", assigned.Errors.Select(error => error.Code))}"
            );

        return user;
    }
}

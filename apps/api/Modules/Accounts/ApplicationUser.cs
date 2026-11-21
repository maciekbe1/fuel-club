using Microsoft.AspNetCore.Identity;

namespace FuelClub.Api.Modules.Accounts
{
    public sealed class ApplicationUser : IdentityUser
    {
        public string DisplayName { get; set; } = string.Empty;
    }
}

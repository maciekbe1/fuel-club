using System.Security.Claims;

namespace FuelClub.Api.Modules.Accounts.Application;

public interface IAccountsService
{
    Task<AccountOperationResult> RegisterAsync(RegisterRequest request);
    Task<AccountOperationResult> LoginAsync(LoginRequest request);
    Task<AccountProfile?> GetCurrentUserAsync(ClaimsPrincipal principal);
    Task LogoutAsync();
}

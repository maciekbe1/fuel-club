using FuelClub.Api.Modules.Accounts.Application;

namespace FuelClub.Api.Modules.Accounts;

public static class LogoutEndpoint
{
    public static async Task<IResult> Handle(IAccountsService accountsService)
    {
        await accountsService.LogoutAsync();
        return Results.NoContent();
    }
}

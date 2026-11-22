using FuelClub.Api.Modules.Accounts.Application;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FuelClub.Api.Modules.Accounts
{
    public sealed record MeResponse(
        string Id,
        string? Email,
        string DisplayName,
        IList<string> Roles
    );

    public static class MeEndpoint
    {
        public static async Task<Results<Ok<MeResponse>, UnauthorizedHttpResult>> Handle(
            IAccountsService accountsService,
            HttpContext httpContext
        )
        {
            var user = await accountsService.GetCurrentUserAsync(httpContext.User);
            if (user is null)
            {
                return TypedResults.Unauthorized();
            }

            return TypedResults.Ok(
                new MeResponse(user.Id, user.Email, user.DisplayName, user.Roles)
            );
        }
    }
}

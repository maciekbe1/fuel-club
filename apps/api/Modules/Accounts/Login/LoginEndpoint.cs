using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts.Application;

namespace FuelClub.Api.Modules.Accounts
{
    public static class LoginEndpoint
    {
        public static async Task<IResult> Handle(
            LoginRequest request,
            IAccountsService accountsService
        )
        {
            var result = await accountsService.LoginAsync(request);
            if (!result.Succeeded)
            {
                var status = result.ErrorCode == AccountErrorCodes.RequiredFields
                    ? StatusCodes.Status400BadRequest
                    : StatusCodes.Status401Unauthorized;
                return ApiProblems.Validation("Login failed", result.ErrorCode!, result.Errors, status);
            }
            return Results.NoContent();
        }
    }
}

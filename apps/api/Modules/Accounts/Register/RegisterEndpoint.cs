using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts.Application;

namespace FuelClub.Api.Modules.Accounts
{
    public static class RegisterEndpoint
    {
        public static async Task<IResult> Handle(
            RegisterRequest request,
            IAccountsService accountsService
        )
        {
            var result = await accountsService.RegisterAsync(request);
            if (!result.Succeeded)
            {
                return ApiProblems.Validation(
                    "Registration failed",
                    result.ErrorCode!,
                    result.Errors
                );
            }

            return Results.StatusCode(StatusCodes.Status201Created);
        }
    }
}

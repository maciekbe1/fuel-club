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
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Registration failed",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = result.ErrorCode!,
                        ["errors"] = result.Errors.GroupBy(error => error.Field)
                            .ToDictionary(group => group.Key, group => group.Select(error => new { error.Field, error.Code }).ToArray())
                    }
                );
            }

            return Results.StatusCode(StatusCodes.Status201Created);
        }
    }
}

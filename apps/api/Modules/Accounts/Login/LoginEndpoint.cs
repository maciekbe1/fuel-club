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
                return Results.Problem(
                    statusCode: result.ErrorCode == AccountErrorCodes.RequiredFields
                        ? StatusCodes.Status400BadRequest
                        : StatusCodes.Status401Unauthorized,
                    title: "Login failed",
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = result.ErrorCode!,
                        ["errors"] = result.Errors.GroupBy(error => error.Field)
                            .ToDictionary(group => group.Key, group => group.Select(error => new { error.Field, error.Code }).ToArray())
                    }
                );
            }
            return Results.NoContent();
        }
    }
}

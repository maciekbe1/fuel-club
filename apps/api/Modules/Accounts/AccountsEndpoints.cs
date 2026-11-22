using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Csrf;

namespace FuelClub.Api.Modules.Accounts
{
    public static class AccountsEndpoints
    {
        public static IEndpointRouteBuilder MapAccountsEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup("/api/auth").WithTags("Accounts").RequireCsrf();

            group
                .MapPost("/register", RegisterEndpoint.Handle)
                .RequireRateLimiting(RateLimiting.AuthPolicy)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .Produces(StatusCodes.Status201Created)
                .ProducesProblem(StatusCodes.Status400BadRequest);
            group
                .MapPost("/login", LoginEndpoint.Handle)
                .RequireRateLimiting(RateLimiting.AuthPolicy)
                .ProducesProblem(StatusCodes.Status429TooManyRequests)
                .Produces(StatusCodes.Status204NoContent)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized);
            group.MapPost("/logout", LogoutEndpoint.Handle).RequireAuthorization();
            group.MapGet("/me", MeEndpoint.Handle).RequireAuthorization();

            return app;
        }
    }
}

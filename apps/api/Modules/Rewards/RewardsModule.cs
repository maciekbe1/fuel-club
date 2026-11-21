using FuelClub.Api.Infrastructure.Csrf;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Rewards.Admin;
using FuelClub.Api.Modules.Rewards.Catalog;

namespace FuelClub.Api.Modules.Rewards;

public static class RewardsModule
{
    public static IEndpointRouteBuilder MapRewardsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGroup("/api/rewards")
            .WithTags("Rewards")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.User))
            .MapGet("", CatalogEndpoint.Handle);

        // Admin manages offers; Cashier has no access.
        var admin = app.MapGroup(AdminRewardEndpoints.Path)
            .WithTags("AdminRewards")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin))
            .RequireCsrf();

        admin.MapGet("", AdminRewardEndpoints.GetAll);
        admin.MapGet("/{id:guid}", AdminRewardEndpoints.GetOne)
            .ProducesProblem(StatusCodes.Status404NotFound);
        admin.MapPost("", AdminRewardEndpoints.Create)
            .RequireIdempotency()
            .ProducesProblem(StatusCodes.Status400BadRequest);
        admin.MapPut("/{id:guid}", AdminRewardEndpoints.Update)
            .WithDescription(AdminRewardEndpoints.UpdateDescription)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }
}

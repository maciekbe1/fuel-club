using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Csrf;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Rewards.Admin;
using FuelClub.Api.Modules.Rewards.Catalog;
using FuelClub.Api.Modules.Rewards.Coupons;
using FuelClub.Api.Modules.Rewards.Exchange;
using FuelClub.Api.Modules.Rewards.Infrastructure;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Rewards;

public static class RewardsModule
{
    public static IServiceCollection AddRewards(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RewardsOptions>()
            .Bind(configuration.GetSection(RewardsOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RewardsOptions>, RewardsOptionsValidator>();
        return services;
    }

    public static IEndpointRouteBuilder MapRewardsEndpoints(this IEndpointRouteBuilder app)
    {
        var customer = app.MapGroup("/api/rewards")
            .WithTags("Rewards")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.User))
            .RequireCsrf();
        customer.MapGet("", CatalogEndpoint.Handle);
        customer.MapPost("/{id:guid}/exchange", ExchangeEndpoint.Handle)
            .RequireIdempotency()
            .WithDescription(ExchangeEndpoint.Description)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Responses carry redemption codes.
        var coupons = app.MapGroup(MyCouponsEndpoints.Path)
            .WithTags("Coupons")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.User))
            .RequireCsrf()
            .DisableResponseStoring();
        coupons.MapGet("", MyCouponsEndpoints.GetAll);
        coupons.MapGet("/{id:guid}", MyCouponsEndpoints.GetOne)
            .ProducesProblem(StatusCodes.Status404NotFound);
        coupons.MapPost("/{id:guid}/activate", ActivateEndpoint.Handle)
            .RequireIdempotency()
            .WithDescription(ActivateEndpoint.Description)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

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

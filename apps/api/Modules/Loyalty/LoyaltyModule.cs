using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Csrf;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Card;
using FuelClub.Api.Modules.Loyalty.Cashier;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using FuelClub.Api.Modules.Loyalty.Wallet;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Loyalty;

public static class LoyaltyModule
{
    public static IServiceCollection AddLoyalty(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LoyaltyOptions>()
            .Bind(configuration.GetSection(LoyaltyOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<LoyaltyOptions>, LoyaltyOptionsValidator>();
        services.AddScoped<CustomerLock>();
        services.AddScoped<Points.PointsSpending>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<StationClock>();
        services.AddHostedService<CashierAccountProvisioner>();
        return services;
    }

    public static IEndpointRouteBuilder MapLoyaltyEndpoints(this IEndpointRouteBuilder app)
    {
        // Admin does not inherit Cashier: only accounts with the Cashier role reach this group.
        var cashier = app.MapGroup("/api/cashier")
            .WithTags("Cashier")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Cashier))
            .RequireCsrf();

        cashier
            .MapGet("/station", CashierStationEndpoint.Handle)
            .ProducesProblem(StatusCodes.Status404NotFound);

        cashier
            .MapPost("/fuel-purchases", FuelPurchaseEndpoint.Handle)
            .RequireIdempotency()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        // Customer data requires the User role; an account that also has Cashier or Admin still reads
        // its own data, staff roles alone get 403.
        var me = app.MapGroup("/api/me")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.User))
            .RequireCsrf();

        me.MapGet("/wallet", WalletEndpoints.GetWallet).WithTags("Wallet");
        me.MapGet("/transactions", WalletEndpoints.GetTransactions)
            .WithTags("Wallet")
            .WithDescription(WalletEndpoints.TransactionsDescription);

        var card = me.MapGroup("/loyalty-card").WithTags("LoyaltyCard").DisableResponseStoring();

        card.MapGet("", LoyaltyCardEndpoints.Get).ProducesProblem(StatusCodes.Status404NotFound);
        card.MapPost("", LoyaltyCardEndpoints.Issue)
            .RequireIdempotency()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        card.MapPost("/rotate", LoyaltyCardEndpoints.Rotate)
            .RequireIdempotency()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return app;
    }
}

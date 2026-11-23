using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Csrf;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Support.Agent;
using FuelClub.Api.Modules.Support.Customer;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Support;

/// <summary>Handing a chat over to a person (ADR 0012): customers open cases, agents (role Support) answer.</summary>
public static class SupportModule
{
    public static IServiceCollection AddSupport(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<SupportOptions>()
            .Bind(configuration.GetSection(SupportOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<SupportOptions>, SupportOptionsValidator>();
        services.AddRateLimiter(options =>
            options.AddPolicy(
                CreateCaseEndpoint.RateLimitPolicy,
                http =>
                {
                    var support = http
                        .RequestServices.GetRequiredService<IOptions<SupportOptions>>()
                        .Value;
                    return RateLimiting.PerUser(http, support.CasePermitLimit, support.CaseWindow);
                }
            )
        );
        return services;
    }

    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        var customer = app.MapGroup("/api/support/cases")
            .WithTags("Support")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.User))
            .RequireCsrf();
        customer
            .MapPost("", CreateCaseEndpoint.Handle)
            .RequireIdempotency()
            .RequireRateLimiting(CreateCaseEndpoint.RateLimitPolicy)
            .WithDescription(CreateCaseEndpoint.Description)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .Produces<SupportConflictProblem>(
                StatusCodes.Status409Conflict,
                "application/problem+json"
            )
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        customer
            .MapPost("{id:guid}/close", CloseCaseEndpoint.Handle)
            .WithDescription(CloseCaseEndpoint.Description)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var agent = app.MapGroup("/api/support/agent/cases")
            .WithTags("Support")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Support))
            .RequireCsrf();
        agent
            .MapPost("{id:guid}/claim", ClaimCaseEndpoint.Handle)
            .WithDescription(ClaimCaseEndpoint.Description)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }
}

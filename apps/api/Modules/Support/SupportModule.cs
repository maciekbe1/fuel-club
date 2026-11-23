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
        services.AddScoped<SupportHousekeeping>();
        services.AddHostedService<SupportWorker>();
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
        // One budget for the customer's and the agent's messages, per user.
        services.AddRateLimiter(options =>
            options.AddPolicy(
                CustomerCaseEndpoints.MessageRateLimitPolicy,
                http =>
                {
                    var support = http
                        .RequestServices.GetRequiredService<IOptions<SupportOptions>>()
                        .Value;
                    return RateLimiting.PerUser(
                        http,
                        support.MessagePermitLimit,
                        support.MessageWindow
                    );
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
            .MapGet("current", CustomerCaseEndpoints.Current)
            .WithDescription(CustomerCaseEndpoints.CurrentDescription);
        customer
            .MapGet("{id:guid}/messages", CustomerCaseEndpoints.Messages)
            .WithDescription(CustomerCaseEndpoints.MessagesDescription)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);
        customer
            .MapPost("{id:guid}/messages", CustomerCaseEndpoints.Send)
            .RequireIdempotency()
            .RequireRateLimiting(CustomerCaseEndpoints.MessageRateLimitPolicy)
            .WithDescription(CustomerCaseEndpoints.SendDescription)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        customer
            .MapPost("{id:guid}/close", CloseCaseEndpoint.Handle)
            .WithDescription(CloseCaseEndpoint.Description)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var agent = app.MapGroup("/api/support/agent/cases")
            .WithTags("Support")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Support))
            .RequireCsrf();
        app.MapGroup("/api/support/agent/queue")
            .WithTags("Support")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Support))
            .MapGet("", AgentCaseEndpoints.Queue)
            .WithDescription(AgentCaseEndpoints.QueueDescription);
        agent
            .MapGet("{id:guid}/messages", AgentCaseEndpoints.Messages)
            .WithDescription(AgentCaseEndpoints.MessagesDescription)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        agent
            .MapPost("{id:guid}/messages", AgentCaseEndpoints.Send)
            .RequireIdempotency()
            .RequireRateLimiting(CustomerCaseEndpoints.MessageRateLimitPolicy)
            .WithDescription(AgentCaseEndpoints.SendDescription)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        agent
            .MapPost("{id:guid}/release", AgentCaseEndpoints.Release)
            .WithDescription(AgentCaseEndpoints.ReleaseDescription)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        agent
            .MapPost("{id:guid}/close", AgentCaseEndpoints.Close)
            .WithDescription(AgentCaseEndpoints.CloseDescription)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        agent
            .MapPost("{id:guid}/claim", ClaimCaseEndpoint.Handle)
            .WithDescription(ClaimCaseEndpoint.Description)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return app;
    }
}

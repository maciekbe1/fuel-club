using FuelClub.Api.Infrastructure.Csrf;
using FuelClub.Api.Modules.Accounts;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Observability;

/// <summary>Security events, anomaly rules, review queue and retention (ADR 0009).</summary>
public static class ObservabilityModule
{
    public static IServiceCollection AddObservability(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection(ObservabilityOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<ObservabilityOptions>,
            ObservabilityOptionsValidator
        >();
        services.AddSingleton<IProblemDetailsService, ProblemCodeCapture>();
        services.AddSingleton<AnomalyAlerts>();
        services.AddSingleton<SecurityEventBudget>();
        services.AddScoped<AnomalyRules>();
        services.AddScoped<ObservabilityRetention>();
        services.AddHostedService<ObservabilityWorker>();
        return services;
    }

    /// <summary>First in the pipeline, so it also sees 401/403 from authentication and 429.</summary>
    public static IApplicationBuilder UseSecurityEvents(this IApplicationBuilder app) =>
        app.UseMiddleware<SecurityEventsMiddleware>();

    public static IEndpointRouteBuilder MapObservabilityEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin/observability")
            .WithTags("Observability")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin))
            .RequireCsrf();
        admin
            .MapGet("", ObservabilityEndpoints.Summary)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithDescription(
                "Metrics over the last `hours` (1-720, default 24), ADR 0018: turns (p50/p95, model steps), "
                    + "tool calls, cost (demo: not applicable; no usage or price: unknown), conversations "
                    + "(handoff once per conversation; Idle is not a resolution), security events and the "
                    + "anomaly rules. `traffic` real (default) | synthetic | all, `runId`, `model`, "
                    + "`promptVersion` filter turns and conversations; security and anomalies are not "
                    + "filtered. Metadata only, users as pseudonyms."
            );
        admin.MapGet("/review", ObservabilityEndpoints.Queue);
        admin
            .MapPost("/review/{id:guid}", ObservabilityEndpoints.MarkReviewed)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }
}

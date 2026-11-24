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
        admin
            .MapGet("/conversations", ConversationTraces.List)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .WithDescription(
                "Conversations started in the last `hours` (1-720, default 24), newest first, with the "
                    + "same `traffic`, `runId`, `model`, `promptVersion` filters as the summary (model and "
                    + "prompt: one turn of the conversation has both). `suspicious=true`: only conversations with a "
                    + "failed turn or a tool that did not succeed (the review queue's rule). Keyset pages: "
                    + "`limit` 1-100 (default 25), `nextCursor` gives the next page; rows added later show "
                    + "up after reloading the first page. Metadata and pseudonyms only; the support case "
                    + "only as `hasCase`."
            );
        admin
            .MapGet("/conversations/{id:guid}", ConversationTraces.Get)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .WithDescription(
                "One conversation with its turns in order: timing, outcome, model steps, tokens, cost "
                    + "and tool calls (name, status, duration). No text, arguments or results."
            );
        admin.MapGet("/review", ObservabilityEndpoints.Queue);
        admin
            .MapPost("/review/{id:guid}", ObservabilityEndpoints.MarkReviewed)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return app;
    }
}

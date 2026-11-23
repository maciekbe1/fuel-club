using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Csrf;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant.Chat;
using FuelClub.Api.Modules.Assistant.Confirmation;
using FuelClub.Api.Modules.Assistant.Model;
using FuelClub.Api.Modules.Assistant.Tools;
using FuelClub.Api.Modules.Assistant.Tracing;
using FuelClub.Api.Modules.Rewards.Coupons;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Assistant;

/// <summary>Chat with tools over the session user's data (ADR 0007).</summary>
public static class AssistantModule
{
    public static IServiceCollection AddAssistant(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<AssistantOptions>()
            .Bind(configuration.GetSection(AssistantOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<AssistantOptions>, AssistantOptionsValidator>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<ConfirmationTokens>();
        services.AddSingleton<TracePseudonyms>();
        // The demo model is the default; Assistant:Provider=gemini switches to the Gemini adapter.
        // Chosen from the bound options when resolved, so every configuration source counts.
        services.AddHttpClient(GeminiChatModel.ClientName);
        services.AddSingleton<DemoChatModel>();
        services.AddTransient<IChatModel>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<AssistantOptions>>();
            return options.Value.Provider == Providers.Gemini
                ? new GeminiChatModel(
                    provider
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient(GeminiChatModel.ClientName),
                    options
                )
                : provider.GetRequiredService<DemoChatModel>();
        });
        services.AddScoped<AssistantTools>();
        services.AddScoped<ChatService>();
        services.AddRateLimiter(options =>
            options.AddPolicy(
                ChatEndpoints.RateLimitPolicy,
                http =>
                {
                    var assistant = http
                        .RequestServices.GetRequiredService<IOptions<AssistantOptions>>()
                        .Value;
                    return RateLimiting.PerUser(
                        http,
                        assistant.ChatPermitLimit,
                        assistant.ChatWindow
                    );
                }
            )
        );
        return services;
    }

    public static IEndpointRouteBuilder MapAssistantEndpoints(this IEndpointRouteBuilder app)
    {
        var chat = app.MapGroup("/api/chat")
            .WithTags("Assistant")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.User))
            .RequireCsrf();
        chat.MapPost("", ChatEndpoints.Chat)
            .RequireRateLimiting(ChatEndpoints.RateLimitPolicy)
            .WithDescription(ChatEndpoints.ChatDescription)
            .Produces<ChatStreamContract>(StatusCodes.Status200OK, "text/event-stream")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        // The response carries the coupon code: no-store on the group, so replays get it too.
        var confirmations = app.MapGroup("/api/chat/confirmations")
            .WithTags("Assistant")
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.User))
            .RequireCsrf()
            .DisableResponseStoring();
        confirmations
            .MapPost("", ChatEndpoints.Confirm)
            .RequireIdempotency()
            .WithDescription(ChatEndpoints.ConfirmDescription)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status410Gone)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }
}

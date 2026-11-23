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
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

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
        // Transient answers (429, 5xx, 408, connection errors) are retried with backoff and a short
        // Retry-After; other 4xx are not. GeminiChatModel's RequestTimeout bounds all attempts of a step.
        var gemini = services.AddHttpClient(GeminiChatModel.ClientName);
        gemini.AddResilienceHandler(
            "gemini",
            (pipeline, context) =>
            {
                var logger = context
                    .ServiceProvider.GetRequiredService<ILoggerFactory>()
                    .CreateLogger<GeminiChatModel>();
                pipeline.AddRetry(
                    new HttpRetryStrategyOptions
                    {
                        MaxRetryAttempts = 2,
                        BackoffType = DelayBackoffType.Exponential,
                        Delay = TimeSpan.FromMilliseconds(500),
                        MaxDelay = GeminiRetryMaxDelay,
                        UseJitter = true,
                        ShouldHandle = args =>
                            ValueTask.FromResult(
                                HttpClientResiliencePredicates.IsTransient(args.Outcome)
                                    && !LongRetryAfter(args.Outcome.Result)
                            ),
                        OnRetry = args =>
                        {
                            // Status or exception type only: bodies may echo the request.
                            logger.LogWarning(
                                "Gemini attempt {Attempt} failed ({Status}); retrying in {Delay}.",
                                args.AttemptNumber + 1,
                                args.Outcome.Result is { } r
                                    ? (int)r.StatusCode
                                    : args.Outcome.Exception?.GetType().Name,
                                args.RetryDelay
                            );
                            return default;
                        },
                    }
                );
            }
        );
        // Added after the retry pipeline, so it runs inside it: it sees every attempt of a step.
        gemini.AddHttpMessageHandler<GeminiAttempts>();
        services.AddTransient<GeminiAttempts>();
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
        services.AddScoped<Conversations.Conversations>();
        services
            .AddOptions<ModelPricingOptions>()
            .Bind(configuration.GetSection(ModelPricingOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ModelPricingOptions>, ModelPricingValidator>();
        services.AddSingleton<ModelPricing>();
        services.AddRateLimiter(options =>
        {
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
            );
            // Its own bucket with the chat's numbers: starting a conversation must not eat the
            // turns, and a turn needs a conversation, so it cannot be lower.
            options.AddPolicy(
                Conversations.ConversationEndpoints.RateLimitPolicy,
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
            );
        });
        return services;
    }

    private static readonly TimeSpan GeminiRetryMaxDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// A Retry-After past the backoff cap (quota) is not waited for: the step fails at once
    /// instead of sleeping until RequestTimeout.
    /// </summary>
    private static bool LongRetryAfter(HttpResponseMessage? response)
    {
        var after = response?.Headers.RetryAfter;
        var delay = after?.Delta ?? (after?.Date - DateTimeOffset.UtcNow);
        return delay > GeminiRetryMaxDelay;
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
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        var conversations = chat.MapGroup("/conversations");
        conversations
            .MapPost("", Conversations.ConversationEndpoints.Start)
            .RequireIdempotency()
            .RequireRateLimiting(Conversations.ConversationEndpoints.RateLimitPolicy)
            .WithDescription(Conversations.ConversationEndpoints.StartDescription)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        conversations
            .MapPost("{id:guid}/end", Conversations.ConversationEndpoints.End)
            .WithDescription(Conversations.ConversationEndpoints.EndDescription)
            .ProducesProblem(StatusCodes.Status404NotFound);
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

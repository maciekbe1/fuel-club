using System.Security.Claims;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Assistant.Tracing;
using FuelClub.Api.Modules.Rewards;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Observability;

/// <summary>
/// Lets the pipeline see the ProblemDetails code of a response without changing it: every
/// ProblemHttpResult asks IProblemDetailsService first, this one records the code and declines,
/// so the result writes itself exactly as before.
/// </summary>
public sealed class ProblemCodeCapture(IEnumerable<IProblemDetailsWriter> writers)
    : IProblemDetailsService
{
    public const string ItemKey = "fuelclub.problem.code";

    // Writers registered by AddProblemDetails still write (as the framework service would); without
    // them, as now, it declines and ProblemHttpResult writes itself.
    private readonly IProblemDetailsWriter[] _writers = writers.ToArray();

    public async ValueTask WriteAsync(ProblemDetailsContext context)
    {
        if (!await TryWriteAsync(context))
            throw new InvalidOperationException(
                "No IProblemDetailsWriter can write this response."
            );
    }

    public async ValueTask<bool> TryWriteAsync(ProblemDetailsContext context)
    {
        Capture(context);
        foreach (var writer in _writers)
        {
            if (!writer.CanWrite(context))
                continue;
            await writer.WriteAsync(context);
            return true;
        }
        return false;
    }

    private static void Capture(ProblemDetailsContext context)
    {
        if (
            context.ProblemDetails.Extensions.TryGetValue("code", out var code)
            && code is string text
        )
            context.HttpContext.Items[ItemKey] = text;
    }
}

/// <summary>A fixed one-minute budget of stored events; the excess is only counted.</summary>
public sealed class SecurityEventBudget(
    IOptions<ObservabilityOptions> options,
    TimeProvider clock,
    ILogger<SecurityEventBudget> logger
)
{
    private readonly Lock _gate = new();
    private long _window = -1;
    private int _used;
    private int _dropped;

    public bool TryTake()
    {
        var window = clock.GetUtcNow().ToUnixTimeSeconds() / 60;
        int droppedBefore = 0;
        bool taken;
        lock (_gate)
        {
            if (window != _window)
            {
                droppedBefore = _dropped;
                _window = window;
                _used = 0;
                _dropped = 0;
            }
            taken = _used < options.Value.MaxSecurityEventsPerMinute;
            if (taken)
                _used++;
            else
                _dropped++;
        }
        if (droppedBefore > 0)
            logger.LogWarning(
                "Security events over the per-minute limit, not stored: {Dropped}",
                droppedBefore
            );
        return taken;
    }
}

/// <summary>
/// Records rejected requests as security events after the response is sent (OnCompleted), in its
/// own scope: the client never waits for it and a failed write is only logged.
/// </summary>
public sealed class SecurityEventsMiddleware(
    RequestDelegate next,
    IServiceScopeFactory scopes,
    SecurityEventBudget budget,
    TracePseudonyms pseudonyms,
    TimeProvider clock,
    ILogger<SecurityEventsMiddleware> logger
)
{
    public async Task InvokeAsync(HttpContext http)
    {
        http.Response.OnCompleted(() => RecordAsync(http));
        await next(http);
    }

    public static SecurityEventKind? Classify(HttpContext http, string? code) =>
        http.Response.StatusCode switch
        {
            StatusCodes.Status403Forbidden => SecurityEventKind.AccessDenied,
            StatusCodes.Status401Unauthorized when RoutePattern(http) == LoginRoute =>
                SecurityEventKind.LoginFailed,
            StatusCodes.Status409Conflict when code == IdempotencyErrorCodes.KeyReused =>
                SecurityEventKind.IdempotencyConflict,
            StatusCodes.Status409Conflict when code == RewardsErrorCodes.CouponRedeemed =>
                SecurityEventKind.RepeatedRedemption,
            StatusCodes.Status429TooManyRequests => SecurityEventKind.RateLimited,
            // Anonymous 401 elsewhere is the normal "not signed in" answer (e.g. /api/auth/me).
            _ => null,
        };

    public const string LoginRoute = "/api/auth/login";

    /// <summary>The matched route ("/api/me/coupons/{id:guid}/activate"), not the path with ids.</summary>
    private static string? RoutePattern(HttpContext http) =>
        (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText;

    private async Task RecordAsync(HttpContext http)
    {
        var code = http.Items[ProblemCodeCapture.ItemKey] as string;
        if (Classify(http, code) is not { } kind || !budget.TryTake())
            return;
        try
        {
            var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
            using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SecurityEvents.Add(
                new SecurityEvent
                {
                    Id = Guid.CreateVersion7(),
                    OccurredAt = clock.GetUtcNow(),
                    Kind = kind,
                    Status = http.Response.StatusCode,
                    Code = code,
                    Route = Truncate(RoutePattern(http) ?? "(no route)", 200),
                    UserPseudonym = userId is null ? null : pseudonyms.For(userId),
                }
            );
            await db.SaveChangesAsync(limit.Token);
        }
        catch (Exception error)
        {
            logger.LogWarning("Security event not saved: {Error}", error.GetType().Name);
        }
    }

    private static string Truncate(string text, int length) =>
        text.Length <= length ? text : text[..length];
}

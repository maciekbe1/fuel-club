using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;

namespace FuelClub.Api.Infrastructure;

public static class RateLimiting
{
    public const string ExceededCode = "rate_limit.exceeded";

    /// <summary>
    /// Shared rejection: ProblemDetails 429 with Retry-After. Modules add their own policies. The
    /// middleware runs after authorization, so policies can partition by the authenticated user.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services) =>
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    context.HttpContext.Response.Headers.RetryAfter = (
                        (int)Math.Ceiling(retryAfter.TotalSeconds)
                    ).ToString(CultureInfo.InvariantCulture);
                await ApiProblems
                    .Problem(
                        StatusCodes.Status429TooManyRequests,
                        "Too many requests",
                        ExceededCode
                    )
                    .ExecuteAsync(context.HttpContext);
            };
        });

    /// <summary>One fixed window per authenticated user.</summary>
    public static RateLimitPartition<string> PerUser(
        HttpContext http,
        int permitLimit,
        TimeSpan window
    ) =>
        RateLimitPartition.GetFixedWindowLimiter(
            http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }
        );
}

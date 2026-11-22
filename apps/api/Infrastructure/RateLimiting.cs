using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Infrastructure;

/// <summary>Limits shared by several modules (section "RateLimits").</summary>
public sealed class RateLimitOptions
{
    public const string Section = "RateLimits";

    /// <summary>Login and registration attempts per client IP; lockout per account is separate (Identity).</summary>
    public int AuthPermitLimit { get; set; } = 10;
    public TimeSpan AuthWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Fuel purchases (and coupon redemptions with them) per cashier.</summary>
    public int PurchasePermitLimit { get; set; } = 60;
    public TimeSpan PurchaseWindow { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class RateLimitOptionsValidator : IValidateOptions<RateLimitOptions>
{
    public ValidateOptionsResult Validate(string? name, RateLimitOptions o)
    {
        var errors = new List<string>();
        if (o.AuthPermitLimit is < 1 or > 100_000)
            errors.Add("RateLimits:AuthPermitLimit must be 1-100000.");
        if (o.PurchasePermitLimit is < 1 or > 100_000)
            errors.Add("RateLimits:PurchasePermitLimit must be 1-100000.");
        if (o.AuthWindow <= TimeSpan.Zero || o.AuthWindow > TimeSpan.FromHours(1))
            errors.Add("RateLimits:AuthWindow must be > 0 and <= 1 h.");
        if (o.PurchaseWindow <= TimeSpan.Zero || o.PurchaseWindow > TimeSpan.FromHours(1))
            errors.Add("RateLimits:PurchaseWindow must be > 0 and <= 1 h.");
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

public static class RateLimiting
{
    public const string ExceededCode = "rate_limit.exceeded";
    public const string AuthPolicy = "auth";
    public const string PurchasePolicy = "fuel-purchase";

    /// <summary>
    /// Shared rejection: ProblemDetails 429 with Retry-After. Modules add their own policies. The
    /// middleware runs after authorization, so policies can partition by the authenticated user.
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services
            .AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RateLimitOptions>, RateLimitOptionsValidator>();
        return services.AddRateLimiter(options =>
        {
            // The client address after UseForwardedHeaders (ClientAddress), not the Next server.
            options.AddPolicy(
                AuthPolicy,
                http =>
                {
                    var limits = Limits(http);
                    return PerKey(
                        http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                        limits.AuthPermitLimit,
                        limits.AuthWindow
                    );
                }
            );
            options.AddPolicy(
                PurchasePolicy,
                http =>
                {
                    var limits = Limits(http);
                    return PerUser(http, limits.PurchasePermitLimit, limits.PurchaseWindow);
                }
            );
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
    }

    private static RateLimitOptions Limits(HttpContext http) =>
        http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

    /// <summary>One fixed window per authenticated user.</summary>
    public static RateLimitPartition<string> PerUser(
        HttpContext http,
        int permitLimit,
        TimeSpan window
    ) =>
        PerKey(
            http.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anonymous",
            permitLimit,
            window
        );

    private static RateLimitPartition<string> PerKey(
        string key,
        int permitLimit,
        TimeSpan window
    ) =>
        RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = window,
                QueueLimit = 0,
                AutoReplenishment = true,
            }
        );
}

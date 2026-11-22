namespace FuelClub.Api.Infrastructure;

public static class NoStore
{
    /// <summary>
    /// Cache-Control: no-store for responses with secrets (card and coupon codes), including errors.
    /// Apply it to the group: a group filter runs outside the idempotency filter, so a replayed response
    /// (which skips the endpoint and its own filters) carries it too. It does not affect the client's
    /// in-memory cache (TanStack Query).
    /// </summary>
    public static TBuilder DisableResponseStoring<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(
            async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            }
        );
}

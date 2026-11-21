using Microsoft.AspNetCore.Antiforgery;

namespace FuelClub.Api.Infrastructure.Csrf;

public static class Csrf
{
    public const string HeaderName = "X-CSRF-TOKEN";
    public const string InvalidCode = "csrf.invalid";

    public static IServiceCollection AddCsrf(this IServiceCollection services) =>
        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            options.Cookie.Name = "fuelclub.csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        });

    public static IEndpointRouteBuilder MapCsrfEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
            {
                var tokens = antiforgery.GetAndStoreTokens(context);
                context.Response.Headers.CacheControl = "no-store";
                return TypedResults.Ok(new CsrfTokenResponse(tokens.RequestToken!));
            })
            .WithTags("Accounts");

        return app;
    }

    public static TBuilder RequireCsrf<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter<TBuilder, CsrfValidationFilter>();
}

public sealed record CsrfTokenResponse(string Token);

internal sealed class CsrfValidationFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        if (HttpMethods.IsGet(http.Request.Method)
            || HttpMethods.IsHead(http.Request.Method)
            || HttpMethods.IsOptions(http.Request.Method))
            return await next(context);

        if (!await antiforgery.IsRequestValidAsync(http))
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Invalid CSRF token",
                extensions: new Dictionary<string, object?> { ["code"] = Csrf.InvalidCode }
            );

        return await next(context);
    }
}

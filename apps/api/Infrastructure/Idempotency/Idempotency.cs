using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace FuelClub.Api.Infrastructure.Idempotency;

public static class Idempotency
{
    public const string HeaderName = "Idempotency-Key";
    public const string ReplayedHeaderName = "Idempotency-Replayed";

    public static IServiceCollection AddIdempotency(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<IdempotencyOptions>()
            .Bind(configuration.GetSection(IdempotencyOptions.Section))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdempotencyOptions>, IdempotencyOptionsValidator>();
        services.AddScoped<IdempotencyStore>();
        return services;
    }

    /// <summary>
    /// Marks an endpoint as idempotent. Authentication, authorization and group filters (CSRF) run
    /// first, so a stored response is replayed only to a caller who is still allowed to call the endpoint.
    /// </summary>
    public static RouteHandlerBuilder RequireIdempotency(this RouteHandlerBuilder builder) =>
        builder
            .WithMetadata(new IdempotentEndpointMetadata())
            .AddEndpointFilter<IdempotencyFilter>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

    /// <summary>The filter re-reads the body for the fingerprint, so it must be buffered before binding.</summary>
    public static IApplicationBuilder UseIdempotencyBodyBuffering(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint()?.Metadata.GetMetadata<IdempotentEndpointMetadata>() is not null)
                context.Request.EnableBuffering();
            await next(context);
        });

    /// <summary>Documents the required header on idempotent operations.</summary>
    public static void AddIdempotencyHeader(this Microsoft.AspNetCore.OpenApi.OpenApiOptions options) =>
        options.AddOperationTransformer((operation, context, _) =>
        {
            if (context.Description.ActionDescriptor.EndpointMetadata.OfType<IdempotentEndpointMetadata>().Any())
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = HeaderName,
                    In = ParameterLocation.Header,
                    Required = true,
                    Description = "UUID chosen by the client; reuse it only to retry the same request.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid" },
                });
            }
            return Task.CompletedTask;
        });

    internal static IResult Problem(int status, string title, string code) => ApiProblems.Problem(status, title, code);
}

public sealed class IdempotentEndpointMetadata;

internal sealed class IdempotencyFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;

        var headerValues = http.Request.Headers[Idempotency.HeaderName];
        if (headerValues.Count == 0 || string.IsNullOrWhiteSpace(headerValues[0]))
            return Idempotency.Problem(400, "Idempotency-Key header is required", IdempotencyErrorCodes.KeyRequired);
        if (headerValues.Count > 1 || !Guid.TryParseExact(headerValues[0], "D", out var key))
            return Idempotency.Problem(400, "Idempotency-Key must be a UUID", IdempotencyErrorCodes.KeyInvalid);

        var userId = http.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Idempotent endpoints require an authenticated user.");

        byte[] fingerprint;
        try
        {
            fingerprint = await ComputeFingerprintAsync(http);
        }
        catch (RequestFingerprintException exception)
        {
            return Idempotency.Problem(400, "Request body is not valid for an idempotent operation", exception.Code);
        }

        var store = http.RequestServices.GetRequiredService<IdempotencyStore>();
        var reservation = await store.ReserveAsync(userId, key, fingerprint, http.RequestAborted);
        switch (reservation.Outcome)
        {
            case ReservationOutcome.Replay:
                return new StoredResponseResult(reservation.Response!, replayed: true);
            case ReservationOutcome.KeyReused:
                return Idempotency.Problem(
                    409, "Idempotency-Key was already used for a different request", IdempotencyErrorCodes.KeyReused);
            case ReservationOutcome.InProgress:
                http.Response.Headers.RetryAfter = "1";
                return Idempotency.Problem(
                    409, "A request with this Idempotency-Key is still in progress", IdempotencyErrorCodes.InProgress);
        }

        StoredResponse response;
        try
        {
            var result = await next(context);
            response = await CaptureAsync(http, result);
        }
        catch
        {
            await store.AbandonAsync();
            throw;
        }

        switch (IdempotencyPolicy.Classify(response.StatusCode))
        {
            case ResponseClass.Success:
                await store.CompleteAsync(response, keepOperationChanges: true);
                break;
            case ResponseClass.StoredFailure:
                await store.CompleteAsync(response, keepOperationChanges: false);
                break;
            default:
                await store.AbandonAsync();
                break;
        }

        return new StoredResponseResult(response, replayed: false);
    }

    private static async Task<byte[]> ComputeFingerprintAsync(HttpContext http)
    {
        var request = http.Request;
        byte[] body = [];
        if (request.Body.CanSeek)
        {
            request.Body.Position = 0;
            using var copy = new MemoryStream();
            await request.Body.CopyToAsync(copy, http.RequestAborted);
            request.Body.Position = 0;
            body = copy.ToArray();
        }
        else if (request.ContentLength is > 0)
            throw new InvalidOperationException("Request body is not buffered; call UseIdempotencyBodyBuffering.");

        var template = (http.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? request.Path.Value ?? "";
        return RequestFingerprint.Compute(
            request.Method,
            template,
            request.RouteValues.Select(pair => KeyValuePair.Create(pair.Key, Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture))),
            request.Query,
            request.ContentType,
            body);
    }

    /// <summary>
    /// Executes the result into a buffer, so the same bytes go to the client and to the store.
    /// Only status, Content-Type, Location and body are kept; cookies and auth headers never are.
    /// </summary>
    private static async Task<StoredResponse> CaptureAsync(HttpContext http, object? result)
    {
        var httpResult = result as IResult
            ?? throw new InvalidOperationException("Idempotent endpoints must return IResult (TypedResults).");

        var response = http.Response;
        var originalStatus = response.StatusCode;
        var originalHeaders = response.Headers.ToList();
        var originalBodyFeature = http.Features.Get<IHttpResponseBodyFeature>();

        using var buffer = new MemoryStream();
        var captureFeature = new StreamResponseBodyFeature(buffer);
        http.Features.Set<IHttpResponseBodyFeature>(captureFeature);
        // The operation has already run: a client that disconnects now must not cancel serialization
        // (results write with RequestAborted), or the outcome would be rolled back instead of stored.
        var requestAborted = http.RequestAborted;
        http.RequestAborted = CancellationToken.None;
        try
        {
            await httpResult.ExecuteAsync(http);
            await captureFeature.CompleteAsync();
        }
        finally
        {
            http.RequestAborted = requestAborted;
            http.Features.Set(originalBodyFeature);
        }

        var captured = new StoredResponse(
            response.StatusCode,
            response.ContentType,
            response.Headers.Location.Count > 0 ? response.Headers.Location.ToString() : null,
            buffer.ToArray());

        response.Headers.Clear();
        foreach (var (name, value) in originalHeaders)
            response.Headers[name] = value;
        response.StatusCode = originalStatus;
        return captured;
    }
}

public enum ResponseClass { Success, StoredFailure, NotStored }

public static class IdempotencyPolicy
{
    /// <summary>
    /// 2xx: stored with the operation's changes. 4xx domain outcomes (404, 409, 422 ...): stored, changes
    /// rolled back. 400 means the request was rejected before the operation (validation), 401/403 never
    /// reach the filter, 408/429 are transient: none of these are stored, so the key stays usable.
    /// 5xx and exceptions are never stored.
    /// </summary>
    public static ResponseClass Classify(int statusCode) => statusCode switch
    {
        >= 200 and < 300 => ResponseClass.Success,
        400 or 401 or 403 or 408 or 429 => ResponseClass.NotStored,
        >= 400 and < 500 => ResponseClass.StoredFailure,
        _ => ResponseClass.NotStored,
    };
}

internal sealed class StoredResponseResult(StoredResponse stored, bool replayed) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        // Already committed; nobody is left to receive the response.
        if (httpContext.RequestAborted.IsCancellationRequested)
            return;

        var response = httpContext.Response;
        response.StatusCode = stored.StatusCode;
        if (stored.ContentType is not null)
            response.ContentType = stored.ContentType;
        if (stored.Location is not null)
            response.Headers.Location = stored.Location;
        if (replayed)
            response.Headers[Idempotency.ReplayedHeaderName] = "true";
        response.ContentLength = stored.Body.Length;
        if (stored.Body.Length > 0)
            await response.Body.WriteAsync(stored.Body);
    }
}

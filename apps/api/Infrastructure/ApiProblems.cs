using Microsoft.AspNetCore.Http.HttpResults;

namespace FuelClub.Api.Infrastructure;

/// <summary>A field error reported in ProblemDetails <c>errors[field][] { field, code }</c>.</summary>
public interface IFieldError
{
    string Field { get; }
    string Code { get; }
}

/// <summary>
/// The one place that shapes error responses: ProblemDetails with <c>code</c> and, for validation,
/// <c>errors</c> grouped by field. Modules keep only their codes and titles.
/// </summary>
public static class ApiProblems
{
    public static ProblemHttpResult Problem(int status, string title, string code) =>
        TypedResults.Problem(statusCode: status, title: title, extensions: new Dictionary<string, object?> { ["code"] = code });

    public static ProblemHttpResult Validation(string title, string code, IEnumerable<IFieldError> errors, int status = StatusCodes.Status400BadRequest) =>
        TypedResults.Problem(
            statusCode: status,
            title: title,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = code,
                ["errors"] = errors.GroupBy(error => error.Field)
                    .ToDictionary(group => group.Key, group => group.Select(error => new { error.Field, error.Code }).ToArray()),
            });

    /// <summary>Transient 503 with Retry-After; idempotency does not store 5xx, so the client retries with the same key.</summary>
    public static ProblemHttpResult Busy(HttpContext http, string title, string code, int retryAfterSeconds = 1)
    {
        http.Response.Headers.RetryAfter = retryAfterSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return Problem(StatusCodes.Status503ServiceUnavailable, title, code);
    }
}

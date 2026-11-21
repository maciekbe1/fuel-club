using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Csrf;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Tests;

public sealed class TestFaults
{
    private int _crashNext;

    public void CrashNext(int count) => Interlocked.Exchange(ref _crashNext, count);

    public bool ShouldCrash() => Interlocked.Decrement(ref _crashNext) >= 0;

    public void Reset() => Interlocked.Exchange(ref _crashNext, 0);
}

public sealed record TestItemRequest(int Amount, string Outcome);

public sealed record TestItemResponse(string Id, Guid ExecutionId);

/// <summary>Test-only endpoints, registered through IAdditionalEndpoints in ApiFactory.</summary>
public sealed class IdempotencyTestEndpoints : IAdditionalEndpoints
{
    public const string Prefix = "/api/test/idempotency";

    public void Map(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup(Prefix).RequireAuthorization().RequireCsrf();

        group.MapPost("/items/{id}", HandleItem).RequireIdempotency();

        group.MapPost("/cashier-items/{id}", HandleItem)
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Cashier))
            .RequireIdempotency();
    }

    private static async Task<IResult> HandleItem(
        string id,
        TestItemRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        TestFaults faults)
    {
        if (request.Amount <= 0)
            return TypedResults.ValidationProblem(
                new Dictionary<string, string[]> { ["amount"] = ["test.amount_invalid"] });

        if (request.Outcome == "conflict")
            return Problem(409, "test.conflict");

        var userId = users.GetUserId(http.User)!;
        // Partial write through raw SQL and through the change tracker.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "LoyaltyAccounts" ("UserId", "Balance") VALUES ({userId}, {request.Amount})
            ON CONFLICT ("UserId") DO UPDATE SET "Balance" = "LoyaltyAccounts"."Balance" + EXCLUDED."Balance"
            """);

        switch (request.Outcome)
        {
            case "partial-conflict":
                return Problem(409, "test.conflict");
            case "server-error":
                return Problem(500, "test.server_error");
            case "disconnect":
            case "disconnect-cancellation-aware":
                // The client goes away after the domain write, before the response is serialized.
                http.Abort();
                break;
        }

        if (request.Outcome == "disconnect-cancellation-aware")
            return new CancellationAwareResult(new TestItemResponse(id, Guid.NewGuid()));

        if (faults.ShouldCrash())
            throw new InvalidOperationException("Injected test failure after a partial write.");

        return TypedResults.Created($"{Prefix}/items/{id}", new TestItemResponse(id, Guid.NewGuid()));
    }

    /// <summary>
    /// Writes like results that honour RequestAborted (streams, custom results). TypedResults JSON
    /// does not observe the token when writing to the capture buffer, so it cannot prove the guard.
    /// </summary>
    private sealed class CancellationAwareResult(TestItemResponse value) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.RequestAborted.ThrowIfCancellationRequested();
            httpContext.Response.StatusCode = StatusCodes.Status201Created;
            await httpContext.Response.WriteAsJsonAsync(value, httpContext.RequestAborted);
        }
    }

    private static IResult Problem(int status, string code) =>
        TypedResults.Problem(statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}

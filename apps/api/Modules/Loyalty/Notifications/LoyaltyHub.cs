using FuelClub.Api.Modules.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace FuelClub.Api.Modules.Loyalty.Notifications;

/// <summary>
/// Sent to the customer once the cashier's purchase committed: what the points are for. Amounts as in
/// the purchase response; <c>Points</c> may be 0 (below 1 zł or fully covered by a coupon).
/// </summary>
public sealed record PointsAwardedEvent(
    Guid PurchaseId,
    int Points,
    decimal GrossAmount,
    decimal Discount,
    decimal PaidAmount,
    decimal Liters,
    DateOnly SaleDate,
    string StationCode
);

/// <summary>
/// Not a response body: documents the hub's server-to-client events (event name = property name,
/// camelCase) so the frontend gets generated types (ADR 0021).
/// </summary>
public sealed record LoyaltyHubContract(PointsAwardedEvent PointsAwarded);

/// <summary>
/// Server-to-client only (ADR 0021): the customer's own purchases. No client methods; a connection
/// reaches only its user (Clients.User by the session's user id).
/// </summary>
[Authorize(Roles = RoleNames.User)]
public sealed class LoyaltyHub : Hub
{
    public const string Path = "/api/loyalty/hub";
}

/// <summary>
/// Sends after the purchase committed (Response.OnCompleted: the idempotency filter commits before
/// the response is written). An idempotent replay never runs the endpoint, so it sends nothing again.
/// Best effort: a failed send is logged (error type only); the history and balance stay correct.
/// </summary>
public sealed class LoyaltyNotifier(IHubContext<LoyaltyHub> hub, ILogger<LoyaltyNotifier> logger)
{
    public static void AfterResponse(HttpContext http, string userId, PointsAwardedEvent awarded)
    {
        var notifier = http.RequestServices.GetRequiredService<LoyaltyNotifier>();
        http.Response.OnCompleted(() =>
            http.Response.StatusCode < StatusCodes.Status400BadRequest
                ? notifier.PointsAwardedAsync(userId, awarded)
                : Task.CompletedTask
        );
    }

    public async Task PointsAwardedAsync(string userId, PointsAwardedEvent awarded)
    {
        try
        {
            await hub.Clients.User(userId).SendAsync("pointsAwarded", awarded);
        }
        catch (Exception exception)
        {
            logger.LogWarning("Loyalty hub send failed: {ErrorType}", exception.GetType().Name);
        }
    }
}

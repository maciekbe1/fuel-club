using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Loyalty.Cashier;

public sealed record CashierStationResponse(string StationCode, string RegisterCode);

public static class CashierStationEndpoint
{
    public static async Task<Results<Ok<CashierStationResponse>, ProblemHttpResult>> Handle(
        HttpContext httpContext,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(httpContext.User);
        var station = await db
            .CashierStations.AsNoTracking()
            .Where(row => row.UserId == userId)
            .Select(row => new CashierStationResponse(row.StationCode, row.RegisterCode))
            .SingleOrDefaultAsync(cancellationToken);

        if (station is null)
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Cashier station not assigned",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = LoyaltyErrorCodes.CashierStationMissing,
                }
            );

        return TypedResults.Ok(station);
    }
}

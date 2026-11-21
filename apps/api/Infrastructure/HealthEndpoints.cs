namespace FuelClub.Api.Infrastructure
{
    public static class HealthEndpoints
    {
        public static IEndpointRouteBuilder MapDatabaseHealthEndpoint(
            this IEndpointRouteBuilder app
        )
        {
            app.MapGet(
                "/health/db",
                async (AppDbContext db, CancellationToken cancellationToken) =>
                {
                    var connected = await db.Database.CanConnectAsync(cancellationToken);

                    return connected
                        ? Results.Ok(new { database = "connected" })
                        : Results.StatusCode(503);
                }
            );

            return app;
        }
    }
}

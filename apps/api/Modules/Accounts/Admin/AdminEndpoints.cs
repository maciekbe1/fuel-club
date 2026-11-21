namespace FuelClub.Api.Modules.Accounts;

public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/admin/access", () => Results.NoContent())
            .RequireAuthorization(policy => policy.RequireRole(RoleNames.Admin));
        return app;
    }
}

namespace FuelClub.Api.Infrastructure;

/// <summary>
/// Endpoints contributed through DI. The application registers none; integration tests use it to add
/// test-only endpoints (e.g. for the idempotency infrastructure) without shipping them.
/// </summary>
public interface IAdditionalEndpoints
{
    void Map(IEndpointRouteBuilder app);
}

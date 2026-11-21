namespace FuelClub.Api.Modules.Loyalty.Domain;

/// <summary>
/// Station and register assigned to a cashier account. The backend reads it from the cashier's
/// session; purchase requests cannot override it.
/// </summary>
public sealed class CashierStation
{
    public string UserId { get; set; } = null!;
    public string StationCode { get; set; } = null!;
    public string RegisterCode { get; set; } = null!;
}

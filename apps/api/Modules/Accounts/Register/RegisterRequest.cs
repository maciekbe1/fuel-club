namespace FuelClub.Api.Modules.Accounts
{
    public sealed record RegisterRequest(string Email, string Password, string DisplayName);
}

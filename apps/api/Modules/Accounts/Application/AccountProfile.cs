namespace FuelClub.Api.Modules.Accounts.Application;

public sealed record AccountProfile(string Id, string? Email, string DisplayName, IList<string> Roles);

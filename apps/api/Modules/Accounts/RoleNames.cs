namespace FuelClub.Api.Modules.Accounts;

public static class RoleNames
{
    public const string User = "User";
    public const string Admin = "Admin";
    public const string Cashier = "Cashier";
    public const string Support = "Support";
    public static readonly string[] All = [User, Admin, Cashier, Support];
}

using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.DemoData;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CashierTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private readonly List<string> _emails = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
    }

    private string NewEmail(string kind)
    {
        var email = $"cashier-{kind}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        return email;
    }

    private HttpClient NewClient() =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

    private async Task<ApplicationUser> EnsureCashierAsync(string email, string station, string register)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = await CashierProvisioning.EnsureCashierAsync(
            scope.ServiceProvider, email, Password, "Test Cashier", station, register);
        await transaction.CommitAsync();
        return user;
    }

    private async Task<IList<string>> RolesOfAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.GetRolesAsync((await users.FindByEmailAsync(email))!);
    }

    private async Task<(string Station, string Register)?> StationOfAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.CashierStations.AsNoTracking().SingleOrDefaultAsync(station => station.UserId == userId);
        return row is null ? null : (row.StationCode, row.RegisterCode);
    }

    [Fact]
    public async Task Station_endpoint_is_available_only_to_cashiers()
    {
        var userEmail = NewEmail("user");
        var adminEmail = NewEmail("admin");
        var cashierEmail = NewEmail("cashier");
        await factory.CreateTestUserAsync(userEmail, Password, RoleNames.User);
        await factory.CreateTestUserAsync(adminEmail, Password, RoleNames.Admin);
        await EnsureCashierAsync(cashierEmail, "ST-01", "K2");

        using var anonymous = NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/cashier/station")).StatusCode);

        using var user = NewClient();
        await user.LoginAsync(userEmail, Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/cashier/station")).StatusCode);

        using var admin = NewClient();
        await admin.LoginAsync(adminEmail, Password);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/cashier/station")).StatusCode);

        using var cashier = NewClient();
        await cashier.LoginAsync(cashierEmail, Password);
        var response = await cashier.GetAsync("/api/cashier/station");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var station = await response.Content.ReadFromJsonAsync<StationResponse>();
        Assert.Equal(new StationResponse("ST-01", "K2"), station);
    }

    [Fact]
    public async Task Provisioning_creates_cashier_with_only_the_cashier_role_and_normalized_station()
    {
        var email = NewEmail("new");
        var user = await EnsureCashierAsync(email, "  st-01 ", "k1\t");

        Assert.Equal([RoleNames.Cashier], await RolesOfAsync(email));
        Assert.Equal(("ST-01", "K1"), await StationOfAsync(user.Id));
    }

    [Fact]
    public async Task Provisioning_again_updates_the_station_from_configuration()
    {
        var email = NewEmail("rerun");
        var user = await EnsureCashierAsync(email, "ST-01", "K1");
        await EnsureCashierAsync(email, "ST-02", "K9");

        Assert.Equal(("ST-02", "K9"), await StationOfAsync(user.Id));
    }

    [Theory]
    [InlineData(RoleNames.User)]
    [InlineData(RoleNames.Admin)]
    public async Task Provisioning_refuses_to_promote_an_existing_account(string existingRole)
    {
        var email = NewEmail("existing");
        await factory.CreateTestUserAsync(email, Password, existingRole);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => EnsureCashierAsync(email, "ST-01", "K1"));

        Assert.Contains("already exists without the Cashier role", error.Message);
        Assert.Equal([existingRole], await RolesOfAsync(email));
    }

    [Theory]
    [InlineData("ST 01", "K1")]
    [InlineData("ST-01", "")]
    [InlineData("ST_01", "K1")]
    public async Task Provisioning_rejects_an_invalid_station(string station, string register)
    {
        var email = NewEmail("invalid");

        await Assert.ThrowsAsync<InvalidOperationException>(() => EnsureCashierAsync(email, station, register));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Null(await users.FindByEmailAsync(email));
    }

    [Fact]
    public async Task Bootstrap_cashier_requires_all_settings_or_none()
    {
        var none = new CashierAccountProvisioner(
            factory.Services.GetRequiredService<IServiceScopeFactory>(), Configuration([]));
        await none.StartAsync(CancellationToken.None);

        var partial = new CashierAccountProvisioner(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            Configuration(new() { ["BootstrapCashier:Email"] = NewEmail("partial"), ["BootstrapCashier:Password"] = Password }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => partial.StartAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Demo_accounts_are_never_created_outside_development()
    {
        var seeder = DemoSeeder(Environments.Production);

        await seeder.StartAsync(CancellationToken.None);

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (email, _, _) in DemoAccountsSeeder.Accounts)
            Assert.Null(await users.FindByEmailAsync(email));
    }

    [Fact]
    public async Task Demo_accounts_are_created_in_development_with_expected_roles()
    {
        _emails.AddRange(DemoAccountsSeeder.Accounts.Select(account => account.Email));
        var seeder = DemoSeeder(Environments.Development);

        await seeder.StartAsync(CancellationToken.None);
        await seeder.StartAsync(CancellationToken.None);

        foreach (var (email, _, role) in DemoAccountsSeeder.Accounts)
            Assert.Equal([role], await RolesOfAsync(email));

        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var cashier = await users.FindByEmailAsync("kasjer@demo.fuelclub.test");
        Assert.Equal(
            (DemoAccountsSeeder.CashierStationCode, DemoAccountsSeeder.CashierRegisterCode),
            await StationOfAsync(cashier!.Id));
    }

    private DemoAccountsSeeder DemoSeeder(string environmentName) =>
        new(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new TestHostEnvironment(environmentName),
            Configuration(new() { [DemoAccountsSeeder.EnabledKey] = "true", [DemoAccountsSeeder.PasswordKey] = Password }),
            NullLogger<DemoAccountsSeeder>.Instance);

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private sealed record StationResponse(string StationCode, string RegisterCode);

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "FuelClub.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

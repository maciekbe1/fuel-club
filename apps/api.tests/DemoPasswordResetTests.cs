using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.DemoData;
using FuelClub.Api.Modules.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class DemoPasswordResetTests(ApiFactory factory) : IAsyncLifetime
{
    private const string OldPassword = "Old-Demo-Password1!";
    private const string NewPassword = "New-Demo-Password2!";

    public async Task InitializeAsync()
    {
        var seeder = new DemoAccountsSeeder(
            factory.Services.GetRequiredService<IServiceScopeFactory>(),
            new TestHostEnvironment(Environments.Development),
            Configuration(OldPassword),
            NullLogger<DemoAccountsSeeder>.Instance
        );
        await seeder.StartAsync(CancellationToken.None);
    }

    public async Task DisposeAsync()
    {
        foreach (var (email, _, _) in DemoAccountsSeeder.Accounts)
            await factory.DeleteTestUserAsync(email);
    }

    private sealed record Snapshot(string Id, string Email, IList<string> Roles, string? Station);

    private async Task<List<Snapshot>> SnapshotAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var result = new List<Snapshot>();
        foreach (var (email, _, _) in DemoAccountsSeeder.Accounts)
        {
            var user = (await users.FindByEmailAsync(email))!;
            var station = await db
                .CashierStations.AsNoTracking()
                .Where(row => row.UserId == user.Id)
                .Select(row => row.StationCode + "/" + row.RegisterCode)
                .SingleOrDefaultAsync();
            result.Add(new Snapshot(user.Id, email, await users.GetRolesAsync(user), station));
        }
        return result;
    }

    private async Task<bool> AllPasswordsAreAsync(string password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var (email, _, _) in DemoAccountsSeeder.Accounts)
            if (!await users.CheckPasswordAsync((await users.FindByEmailAsync(email))!, password))
                return false;
        return true;
    }

    private Task<int> RunAsync(string environment, string? password) =>
        DemoPasswordReset.RunAsync(
            factory.Services,
            new TestHostEnvironment(environment),
            Configuration(password)
        );

    [Fact]
    public async Task Resets_passwords_keeping_ids_roles_and_station()
    {
        var before = await SnapshotAsync();

        Assert.Equal(0, await RunAsync(Environments.Development, NewPassword));

        Assert.True(await AllPasswordsAreAsync(NewPassword));
        Assert.False(await AllPasswordsAreAsync(OldPassword));
        var after = await SnapshotAsync();
        Assert.Equal(
            before.Select(s => (s.Id, s.Email, string.Join(",", s.Roles), s.Station)),
            after.Select(s => (s.Id, s.Email, string.Join(",", s.Roles), s.Station))
        );
        Assert.Contains(
            after,
            s =>
                s.Station
                == $"{DemoAccountsSeeder.CashierStationCode}/{DemoAccountsSeeder.CashierRegisterCode}"
        );
    }

    [Fact]
    public async Task Refuses_outside_development()
    {
        Assert.Equal(1, await RunAsync(Environments.Production, NewPassword));
        Assert.True(await AllPasswordsAreAsync(OldPassword));
    }

    [Fact]
    public async Task Refuses_without_configured_password()
    {
        Assert.Equal(1, await RunAsync(Environments.Development, null));
        Assert.True(await AllPasswordsAreAsync(OldPassword));
    }

    [Fact]
    public async Task Rejected_password_changes_no_account()
    {
        Assert.Equal(1, await RunAsync(Environments.Development, "short"));
        Assert.True(await AllPasswordsAreAsync(OldPassword));
    }

    private static IConfiguration Configuration(string? password) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    [DemoAccountsSeeder.EnabledKey] = "true",
                    [DemoAccountsSeeder.PasswordKey] = password,
                }
            )
            .Build();

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "FuelClub.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

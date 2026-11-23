using FuelClub.Api.Infrastructure.DemoData;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Simulator;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class SimulationPasswordResetTests(ApiFactory factory) : IAsyncLifetime
{
    private const string OldSecret = "old-simulation-secret-123";
    private const string NewSecret = "new-simulation-secret-456";
    private readonly string _sim = $"sim-reset-{Guid.NewGuid():N}@sim.fuelclub.test";
    private readonly string _other = $"reset-other-{Guid.NewGuid():N}@example.test";
    private const string OtherPassword = "Other-Password1!";

    public async Task InitializeAsync()
    {
        await factory.CreateTestUserAsync(
            _sim,
            Population.PasswordFor(OldSecret, _sim),
            RoleNames.User
        );
        await factory.CreateTestUserAsync(_other, OtherPassword, RoleNames.User);
    }

    public async Task DisposeAsync()
    {
        await factory.DeleteTestUserAsync(_sim);
        await factory.DeleteTestUserAsync(_other);
    }

    [Fact]
    public void Derives_the_same_password_as_the_simulator() =>
        Assert.Equal(
            Population.PasswordFor(NewSecret, _sim),
            SimulationPasswordReset.PasswordFor(NewSecret, _sim)
        );

    [Fact]
    public async Task Sets_simulator_accounts_to_the_new_secret_and_changes_their_stamp_only()
    {
        var stamp = await StampAsync(_sim);

        Assert.Equal(0, await RunAsync(Environments.Development, NewSecret));

        Assert.True(await PasswordIsAsync(_sim, Population.PasswordFor(NewSecret, _sim)));
        Assert.False(await PasswordIsAsync(_sim, Population.PasswordFor(OldSecret, _sim)));
        // A new security stamp: cookies issued before stop validating.
        Assert.NotEqual(stamp, await StampAsync(_sim));
        Assert.True(await PasswordIsAsync(_other, OtherPassword));
    }

    [Theory]
    [InlineData("Production", NewSecret)]
    [InlineData("Development", null)]
    [InlineData("Development", "short")]
    public async Task Refuses_outside_development_or_without_a_secret(
        string environment,
        string? secret
    )
    {
        Assert.Equal(1, await RunAsync(environment, secret));
        Assert.True(await PasswordIsAsync(_sim, Population.PasswordFor(OldSecret, _sim)));
    }

    private Task<int> RunAsync(string environment, string? secret) =>
        SimulationPasswordReset.RunAsync(
            factory.Services,
            new TestHostEnvironment(environment),
            new ConfigurationBuilder()
                .AddInMemoryCollection(
                    new Dictionary<string, string?> { [SimulationPasswordReset.SecretKey] = secret }
                )
                .Build()
        );

    private async Task<bool> PasswordIsAsync(string email, string password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.CheckPasswordAsync((await users.FindByEmailAsync(email))!, password);
    }

    private async Task<string?> StampAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.SecurityStamp;
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "FuelClub.Api.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

using FuelClub.Api.Infrastructure.Idempotency;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Tests;

public sealed class IdempotencyOptionsValidatorTests
{
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    [InlineData("00:00:00.0005")]
    [InlineData("00:00:30.001")]
    [InlineData("01:00:00")]
    public void Rejects_out_of_range_lock_timeout(string value) =>
        Assert.True(Validate(value).Failed);

    [Theory]
    [InlineData("00:00:00.001")]
    [InlineData("00:00:05")]
    [InlineData("00:00:30")]
    public void Accepts_lock_timeout_in_range(string value) =>
        Assert.True(Validate(value).Succeeded);

    private static ValidateOptionsResult Validate(string value) =>
        new IdempotencyOptionsValidator().Validate(
            null,
            new IdempotencyOptions { LockTimeout = TimeSpan.Parse(value) }
        );
}

[Collection(DatabaseCollection.Name)]
public sealed class IdempotencyOptionsStartupTests(ApiFactory factory)
{
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("00:05:00")]
    public void Api_refuses_to_start_with_invalid_lock_timeout(string value)
    {
        // Added after ApiFactory's in-memory settings, so it overrides them.
        using var misconfigured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["Idempotency:LockTimeout"] = value }
                    )
            )
        );

        var error = Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());
        Assert.Contains("LockTimeout", error.Message);
    }
}

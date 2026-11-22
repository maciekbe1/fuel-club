using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CustomerLockTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Card = "/api/me/loyalty-card";
    private readonly List<string> _emails = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
    }

    private sealed record CardResponse(string Code);

    private sealed record ProblemCode(string? Code);

    private async Task<(HttpClient Client, string UserId)> LoggedInAsync()
    {
        var email = $"card-lock-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, RoleNames.User);
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (client, (await users.FindByEmailAsync(email))!.Id);
    }

    private static async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string url,
        string key
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add(Idempotency.HeaderName, key);
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private async Task<int> StoredRecordsAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<AppDbContext>()
            .IdempotencyRecords.CountAsync(r => r.UserId == userId);
    }

    /// <summary>Holds the customer lock in another connection, like a concurrent purchase or rotation.</summary>
    private async Task<IAsyncDisposable> HoldCustomerLockAsync(string userId)
    {
        var connection = new NpgsqlConnection(
            factory
                .Services.GetRequiredService<IConfiguration>()
                .GetConnectionString("DefaultConnection")
        );
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))",
            connection,
            transaction
        );
        command.Parameters.AddWithValue("key", CustomerLock.Key(userId));
        await command.ExecuteNonQueryAsync();
        return new Holder(connection, transaction);
    }

    private sealed class Holder(NpgsqlConnection connection, NpgsqlTransaction transaction)
        : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }

    [Fact]
    public async Task Rotation_waiting_for_a_busy_customer_times_out_with_a_retryable_503()
    {
        var (client, userId) = await LoggedInAsync();
        var issued = (
            await (
                await PostAsync(client, Card, Guid.NewGuid().ToString())
            ).Content.ReadFromJsonAsync<CardResponse>()
        )!.Code;
        var key = Guid.NewGuid().ToString();
        var recordsBefore = await StoredRecordsAsync(userId);

        await using (await HoldCustomerLockAsync(userId))
        {
            var busy = await PostAsync(client, $"{Card}/rotate", key);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
            Assert.Equal(
                LoyaltyErrorCodes.CustomerBusy,
                (await busy.Content.ReadFromJsonAsync<ProblemCode>())!.Code
            );
            // Set by the handler, it must survive the idempotency filter's response capture.
            Assert.Equal("1", busy.Headers.GetValues("Retry-After").Single());
            Assert.Equal(recordsBefore, await StoredRecordsAsync(userId));
            Assert.Equal(issued, (await client.GetFromJsonAsync<CardResponse>(Card))!.Code);
        }

        var retry = await PostAsync(client, $"{Card}/rotate", key);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.NotEqual(issued, (await retry.Content.ReadFromJsonAsync<CardResponse>())!.Code);
    }

    [Fact]
    public async Task Waiting_for_the_customer_lock_honours_cancellation()
    {
        var (_, userId) = await LoggedInAsync();
        await using var holder = await HoldCustomerLockAsync(userId);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var customerLock = scope.ServiceProvider.GetRequiredService<CustomerLock>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var started = DateTime.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            customerLock.TryAcquireAsync(userId, cancellation.Token)
        );
        Assert.True(
            DateTime.UtcNow - started < TimeSpan.FromMilliseconds(900),
            "Cancellation should win over the lock timeout."
        );
    }

    [Fact]
    public async Task Customer_lock_requires_a_transaction()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var customerLock = scope.ServiceProvider.GetRequiredService<CustomerLock>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            customerLock.TryAcquireAsync("any-user", CancellationToken.None)
        );
    }

    [Fact]
    public async Task Card_codes_and_stored_responses_never_reach_the_logs()
    {
        var (client, _) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();
        var logsBefore = factory.Logs.Messages.Count;

        var issued = (
            await (await PostAsync(client, Card, key)).Content.ReadFromJsonAsync<CardResponse>()
        )!.Code;
        await PostAsync(client, Card, key);
        var rotated = (
            await (
                await PostAsync(client, $"{Card}/rotate", Guid.NewGuid().ToString())
            ).Content.ReadFromJsonAsync<CardResponse>()
        )!.Code;
        await client.GetAsync(Card);

        var messages = factory.Logs.Messages;
        Assert.True(messages.Count > logsBefore, "The capture must see the requests' logs.");
        Assert.Contains(messages, message => message.Contains("LoyaltyCards"));
        Assert.DoesNotContain(
            messages,
            message => message.Contains(issued) || message.Contains(rotated)
        );
    }
}

public sealed class LoyaltyOptionsValidatorTests
{
    [Theory]
    [InlineData("00:00:00", false)]
    [InlineData("00:00:00.0005", false)]
    [InlineData("00:00:30.001", false)]
    [InlineData("00:00:00.001", true)]
    [InlineData("00:00:30", true)]
    public void Customer_lock_timeout_must_be_in_range(string value, bool valid) =>
        Assert.Equal(
            valid,
            new LoyaltyOptionsValidator()
                .Validate(null, new LoyaltyOptions { CustomerLockTimeout = TimeSpan.Parse(value) })
                .Succeeded
        );
}

[Collection(DatabaseCollection.Name)]
public sealed class LoyaltyOptionsStartupTests(ApiFactory factory)
{
    [Fact]
    public void Api_refuses_to_start_with_an_unbounded_customer_lock()
    {
        using var misconfigured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Loyalty:CustomerLockTimeout"] = "00:00:00",
                        }
                    )
            )
        );

        var error = Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());
        Assert.Contains("CustomerLockTimeout", error.Message);
    }
}

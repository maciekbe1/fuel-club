using System.Net;
using System.Net.Http.Json;
using System.Text;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class IdempotencyTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Items = IdempotencyTestEndpoints.Prefix + "/items";
    private readonly List<string> _emails = [];

    public Task InitializeAsync()
    {
        factory.Faults.Reset();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        factory.Faults.Reset();
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
    }

    private async Task<(HttpClient Client, string UserId)> LoggedInAsync(string role = RoleNames.User)
    {
        var email = $"idempotency-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, role);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.LoginAsync(email, Password);
        return (client, await UserIdAsync(email));
    }

    private async Task<string> UserIdAsync(string email)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (await users.FindByEmailAsync(email))!.Id;
    }

    private async Task<decimal?> BalanceAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.LoyaltyAccounts.Where(a => a.UserId == userId).Select(a => (decimal?)a.Balance).SingleOrDefaultAsync();
    }

    private async Task<int> StoredRecordsAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.IdempotencyRecords.CountAsync(r => r.UserId == userId);
    }

    private static string Body(int amount = 10, string outcome = "ok") =>
        $$"""{"amount":{{amount}},"outcome":"{{outcome}}"}""";

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, string url, string json, string? key, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (key is not null)
            request.Headers.Add(Idempotency.HeaderName, key);
        if (csrf)
            request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemCode>())?.Code;

    private sealed record ProblemCode(string? Code);

    private static bool IsReplayed(HttpResponseMessage response) =>
        response.Headers.TryGetValues(Idempotency.ReplayedHeaderName, out var values) && values.Contains("true");

    [Fact]
    public async Task Replay_returns_the_stored_status_body_and_location_without_running_again()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        var first = await SendAsync(client, $"{Items}/A", Body(), key);
        var replay = await SendAsync(client, $"{Items}/A", Body(), key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.False(IsReplayed(first));
        Assert.True(IsReplayed(replay));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(first.Headers.Location, replay.Headers.Location);
        Assert.Equal($"{Items}/A", replay.Headers.Location!.OriginalString);
        Assert.Equal(first.Content.Headers.ContentType, replay.Content.Headers.ContentType);
        Assert.Equal(10m, await BalanceAsync(userId));
    }

    [Fact]
    public async Task Same_key_with_a_different_route_id_is_rejected()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, $"{Items}/A", Body(), key)).StatusCode);
        var other = await SendAsync(client, $"{Items}/B", Body(), key);

        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal(IdempotencyErrorCodes.KeyReused, await CodeAsync(other));
        Assert.Equal(10m, await BalanceAsync(userId));
    }

    [Theory]
    [InlineData("?note=1", "?note=2")]
    [InlineData("", "?note=1")]
    public async Task Same_key_with_a_different_query_is_rejected(string firstQuery, string secondQuery)
    {
        var (client, _) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, $"{Items}/A{firstQuery}", Body(), key)).StatusCode);
        var other = await SendAsync(client, $"{Items}/A{secondQuery}", Body(), key);

        Assert.Equal(IdempotencyErrorCodes.KeyReused, await CodeAsync(other));
    }

    [Fact]
    public async Task Same_key_with_a_different_body_is_rejected()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        await SendAsync(client, $"{Items}/A", Body(10), key);
        var other = await SendAsync(client, $"{Items}/A", Body(11), key);

        Assert.Equal(HttpStatusCode.Conflict, other.StatusCode);
        Assert.Equal(IdempotencyErrorCodes.KeyReused, await CodeAsync(other));
        Assert.Equal(10m, await BalanceAsync(userId));
    }

    [Fact]
    public async Task Reordered_keys_and_whitespace_are_the_same_request()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        await SendAsync(client, $"{Items}/A", """{"amount":10,"outcome":"ok"}""", key);
        var replay = await SendAsync(client, $"{Items}/A", """ { "outcome" : "ok",  "amount" : 10 } """, key);

        Assert.Equal(HttpStatusCode.Created, replay.StatusCode);
        Assert.True(IsReplayed(replay));
        Assert.Equal(10m, await BalanceAsync(userId));
    }

    [Fact]
    public async Task Duplicate_json_property_is_rejected_and_stores_nothing()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        var duplicate = await SendAsync(client, $"{Items}/A", """{"amount":10,"amount":20,"outcome":"ok"}""", key);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(IdempotencyErrorCodes.DuplicateJsonProperty, await CodeAsync(duplicate));
        Assert.Null(await BalanceAsync(userId));
        Assert.Equal(0, await StoredRecordsAsync(userId));
    }

    [Theory]
    [InlineData("""{"amount":10,"Amount":20,"outcome":"ok"}""")]
    [InlineData("""{"Amount":20,"amount":10,"outcome":"ok"}""")]
    [InlineData("""{"amount":10,"outcome":"ok","OUTCOME":"conflict"}""")]
    public async Task Duplicate_property_differing_only_in_case_is_rejected_before_dto_binding_matters(string json)
    {
        var (client, userId) = await LoggedInAsync();

        var response = await SendAsync(client, $"{Items}/A", json, Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(IdempotencyErrorCodes.DuplicateJsonProperty, await CodeAsync(response));
        Assert.Null(await BalanceAsync(userId));
        Assert.Equal(0, await StoredRecordsAsync(userId));
    }

    [Theory]
    [InlineData("disconnect")]
    [InlineData("disconnect-cancellation-aware")]
    public async Task Outcome_is_stored_when_the_client_disconnects_after_the_domain_write(string outcome)
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        try
        {
            using var lost = await SendAsync(client, $"{Items}/A", Body(outcome: outcome), key);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or OperationCanceledException)
        {
            // Expected: the server aborted the connection.
        }

        // The client sees the abort at once; the server keeps going and commits shortly after.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (await StoredRecordsAsync(userId) == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(25);
        Assert.Equal(1, await StoredRecordsAsync(userId));
        Assert.Equal(10m, await BalanceAsync(userId));

        var retry = await SendAsync(client, $"{Items}/A", Body(outcome: outcome), key);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.True(IsReplayed(retry));
        Assert.Equal(10m, await BalanceAsync(userId));
    }

    [Theory]
    [InlineData(null, IdempotencyErrorCodes.KeyRequired)]
    [InlineData("", IdempotencyErrorCodes.KeyRequired)]
    [InlineData("not-a-uuid", IdempotencyErrorCodes.KeyInvalid)]
    [InlineData("0f8fad5bd9cb469fa16570867728950e", IdempotencyErrorCodes.KeyInvalid)]
    public async Task Missing_or_malformed_key_is_rejected(string? key, string code)
    {
        var (client, userId) = await LoggedInAsync();

        var response = await SendAsync(client, $"{Items}/A", Body(), key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(code, await CodeAsync(response));
        Assert.Null(await BalanceAsync(userId));
    }

    [Fact]
    public async Task Domain_error_is_stored_and_replayed()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        var first = await SendAsync(client, $"{Items}/A", Body(outcome: "conflict"), key);
        var replay = await SendAsync(client, $"{Items}/A", Body(outcome: "conflict"), key);

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, replay.StatusCode);
        Assert.True(IsReplayed(replay));
        Assert.Equal("test.conflict", await CodeAsync(replay));
        Assert.Equal(await first.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        Assert.Equal(1, await StoredRecordsAsync(userId));
    }

    [Fact]
    public async Task Partial_write_is_rolled_back_when_the_operation_returns_a_domain_error()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        var first = await SendAsync(client, $"{Items}/A", Body(outcome: "partial-conflict"), key);
        var replay = await SendAsync(client, $"{Items}/A", Body(outcome: "partial-conflict"), key);

        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);
        Assert.True(IsReplayed(replay));
        Assert.Null(await BalanceAsync(userId));
        Assert.Equal(1, await StoredRecordsAsync(userId));
    }

    [Fact]
    public async Task Exception_after_a_partial_write_stores_nothing_and_the_same_key_can_be_retried()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();
        factory.Faults.CrashNext(1);

        var failed = await SendAsync(client, $"{Items}/A", Body(), key);
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.Null(await BalanceAsync(userId));
        Assert.Equal(0, await StoredRecordsAsync(userId));

        var retry = await SendAsync(client, $"{Items}/A", Body(), key);
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.False(IsReplayed(retry));
        Assert.Equal(10m, await BalanceAsync(userId));
    }

    [Fact]
    public async Task Returned_5xx_is_not_stored_and_rolls_back()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        var first = await SendAsync(client, $"{Items}/A", Body(outcome: "server-error"), key);
        var second = await SendAsync(client, $"{Items}/A", Body(outcome: "server-error"), key);

        Assert.Equal(HttpStatusCode.InternalServerError, first.StatusCode);
        Assert.False(IsReplayed(second));
        Assert.Null(await BalanceAsync(userId));
        Assert.Equal(0, await StoredRecordsAsync(userId));
    }

    [Fact]
    public async Task Validation_error_is_not_stored_so_the_corrected_request_can_use_the_same_key()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        Assert.Equal(HttpStatusCode.BadRequest, (await SendAsync(client, $"{Items}/A", Body(0), key)).StatusCode);
        var corrected = await SendAsync(client, $"{Items}/A", Body(10), key);

        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
        Assert.Equal(10m, await BalanceAsync(userId));
    }

    [Fact]
    public async Task Concurrent_requests_with_one_key_run_the_operation_once()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => SendAsync(client, $"{Items}/A", Body(), key)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        var bodies = await Task.WhenAll(responses.Select(response => response.Content.ReadAsStringAsync()));
        Assert.Single(bodies.Distinct());
        Assert.Equal(7, responses.Count(IsReplayed));
        Assert.Equal(10m, await BalanceAsync(userId));
    }

    [Fact]
    public async Task The_same_key_of_another_user_is_a_separate_operation()
    {
        var (anna, annaId) = await LoggedInAsync();
        var (bartek, bartekId) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();

        var first = await SendAsync(anna, $"{Items}/A", Body(), key);
        var second = await SendAsync(bartek, $"{Items}/A", Body(), key);

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(IsReplayed(second));
        Assert.NotEqual(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(10m, await BalanceAsync(annaId));
        Assert.Equal(10m, await BalanceAsync(bartekId));
    }

    [Fact]
    public async Task Waiting_for_a_held_key_times_out_with_a_retryable_error()
    {
        var (client, userId) = await LoggedInAsync();
        var key = Guid.NewGuid();

        await using (var holder = await HoldKeyAsync(userId, key))
        {
            var blocked = await SendAsync(client, $"{Items}/A", Body(), key.ToString());

            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
            Assert.Equal(IdempotencyErrorCodes.InProgress, await CodeAsync(blocked));
            Assert.True(blocked.Headers.Contains("Retry-After"));
            Assert.Null(await BalanceAsync(userId));
        }

        var retry = await SendAsync(client, $"{Items}/A", Body(), key.ToString());
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(10m, await BalanceAsync(userId));
    }

    [Fact]
    public async Task Waiting_for_a_held_key_honours_cancellation()
    {
        var (_, userId) = await LoggedInAsync();
        var key = Guid.NewGuid();
        await using var holder = await HoldKeyAsync(userId, key);

        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IdempotencyStore>();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
        var started = DateTime.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.ReserveAsync(userId, key, new byte[IdempotencyRecord.FingerprintLength], cancellation.Token));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromMilliseconds(900), "Cancellation should win over the lock timeout.");
    }

    [Fact]
    public async Task Replay_still_requires_a_valid_csrf_token()
    {
        var (client, _) = await LoggedInAsync();
        var key = Guid.NewGuid().ToString();
        await SendAsync(client, $"{Items}/A", Body(), key);

        var replay = await SendAsync(client, $"{Items}/A", Body(), key, csrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.Equal("csrf.invalid", await CodeAsync(replay));
        Assert.False(IsReplayed(replay));
    }

    [Fact]
    public async Task Replay_is_refused_after_losing_the_required_role()
    {
        var (client, userId) = await LoggedInAsync(RoleNames.Cashier);
        var key = Guid.NewGuid().ToString();
        var url = $"{IdempotencyTestEndpoints.Prefix}/cashier-items/A";
        Assert.Equal(HttpStatusCode.Created, (await SendAsync(client, url, Body(), key)).StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(userId))!;
            Assert.True((await users.RemoveFromRoleAsync(user, RoleNames.Cashier)).Succeeded);
        }

        var replay = await SendAsync(client, url, Body(), key);

        Assert.Equal(HttpStatusCode.Forbidden, replay.StatusCode);
        Assert.False(IsReplayed(replay));
    }

    [Fact]
    public async Task Anonymous_request_is_rejected_before_idempotency()
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var response = await SendAsync(client, $"{Items}/A", Body(), Guid.NewGuid().ToString());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_documents_the_required_header()
    {
        using var client = factory.CreateClient();
        var document = await client.GetStringAsync("/openapi/v1.json");
        using var json = System.Text.Json.JsonDocument.Parse(document);

        var parameters = json.RootElement.GetProperty("paths").GetProperty($"{Items}/{{id}}")
            .GetProperty("post").GetProperty("parameters").EnumerateArray();
        var header = Assert.Single(parameters, p => p.GetProperty("name").GetString() == Idempotency.HeaderName);
        Assert.Equal("header", header.GetProperty("in").GetString());
        Assert.True(header.GetProperty("required").GetBoolean());
    }

    /// <summary>Reserves the key in an open transaction, like a concurrent request still running.</summary>
    private async Task<KeyHolder> HoldKeyAsync(string userId, Guid key)
    {
        var connectionString = factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand(
            """INSERT INTO "IdempotencyRecords" ("UserId", "IdempotencyKey", "Fingerprint") VALUES (@user, @key, @fingerprint)""",
            connection,
            transaction);
        command.Parameters.AddWithValue("user", userId);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("fingerprint", new byte[IdempotencyRecord.FingerprintLength]);
        await command.ExecuteNonQueryAsync();
        return new KeyHolder(connection, transaction);
    }

    private sealed class KeyHolder(NpgsqlConnection connection, NpgsqlTransaction transaction) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }
}

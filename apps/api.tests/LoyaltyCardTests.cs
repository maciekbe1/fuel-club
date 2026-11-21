using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Loyalty.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class LoyaltyCardTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Card = "/api/me/loyalty-card";
    private const string Rotate = Card + "/rotate";
    private readonly List<string> _emails = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
    }

    private sealed record CardResponse(string Code, DateTimeOffset IssuedAt);

    private sealed record ProblemCode(string? Code);

    private async Task<(HttpClient Client, string UserId)> LoggedInAsync(string role = RoleNames.User)
    {
        var email = $"card-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, role);
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        await client.LoginAsync(email, Password);
        await using var scope = factory.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return (client, (await users.FindByEmailAsync(email))!.Id);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, string? key, bool csrf = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (key is not null)
            request.Headers.Add(Idempotency.HeaderName, key);
        if (csrf)
            request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private static async Task<string> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<CardResponse>())!.Code;

    private static async Task<string?> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemCode>())?.Code;

    private static bool IsReplayed(HttpResponseMessage response) =>
        response.Headers.TryGetValues(Idempotency.ReplayedHeaderName, out var values) && values.Contains("true");

    private async Task<List<LoyaltyCard>> CardsOfAsync(string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.LoyaltyCards.AsNoTracking().Where(card => card.UserId == userId).ToListAsync();
    }

    [Fact]
    public async Task Get_reads_only_and_does_not_create_a_card()
    {
        var (client, userId) = await LoggedInAsync();

        var response = await client.GetAsync(Card);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(LoyaltyErrorCodes.CardMissing, await ErrorOf(response));
        Assert.Empty(await CardsOfAsync(userId));
    }

    [Fact]
    public async Task Issue_creates_a_valid_card_that_get_returns()
    {
        var (client, userId) = await LoggedInAsync();

        var issued = await PostAsync(client, Card, NewKey());

        Assert.Equal(HttpStatusCode.Created, issued.StatusCode);
        Assert.Equal(Card, issued.Headers.Location!.OriginalString);
        var code = await CodeOf(issued);
        Assert.True(LoyaltyCardCode.IsValid(code));
        Assert.Equal(code, await CodeOf(await client.GetAsync(Card)));
        Assert.Single(await CardsOfAsync(userId));
    }

    [Fact]
    public async Task Retry_of_issue_with_the_same_key_returns_the_same_card()
    {
        var (client, userId) = await LoggedInAsync();
        var key = NewKey();

        var first = await PostAsync(client, Card, key);
        var retry = await PostAsync(client, Card, key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.True(IsReplayed(retry));
        Assert.Equal(await CodeOf(first), await CodeOf(retry));
        Assert.Single(await CardsOfAsync(userId));
    }

    [Fact]
    public async Task Issue_with_a_new_key_when_a_card_is_active_is_rejected()
    {
        var (client, userId) = await LoggedInAsync();
        await PostAsync(client, Card, NewKey());

        var second = await PostAsync(client, Card, NewKey());

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal(LoyaltyErrorCodes.CardAlreadyIssued, await ErrorOf(second));
        Assert.Single(await CardsOfAsync(userId));
    }

    [Fact]
    public async Task Concurrent_issues_with_different_keys_create_exactly_one_card()
    {
        var (client, userId) = await LoggedInAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PostAsync(client, Card, NewKey())));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(
            responses.Where(response => response.StatusCode != HttpStatusCode.Created),
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
        Assert.Single(await CardsOfAsync(userId));
    }

    [Fact]
    public async Task Rotation_issues_a_new_code_and_revokes_the_old_one_immediately()
    {
        var (client, userId) = await LoggedInAsync();
        var oldCode = await CodeOf(await PostAsync(client, Card, NewKey()));

        var rotated = await PostAsync(client, Rotate, NewKey());

        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        var newCode = await CodeOf(rotated);
        Assert.NotEqual(oldCode, newCode);
        Assert.Equal(newCode, await CodeOf(await client.GetAsync(Card)));
        var cards = await CardsOfAsync(userId);
        Assert.Equal(2, cards.Count);
        Assert.NotNull(cards.Single(card => card.Code == oldCode).RevokedAt);
        Assert.Null(cards.Single(card => card.Code == newCode).RevokedAt);
    }

    [Fact]
    public async Task Retry_of_rotation_with_the_same_key_rotates_once()
    {
        var (client, userId) = await LoggedInAsync();
        await PostAsync(client, Card, NewKey());
        var key = NewKey();

        var first = await PostAsync(client, Rotate, key);
        var retry = await PostAsync(client, Rotate, key);

        Assert.True(IsReplayed(retry));
        var rotated = await CodeOf(first);
        Assert.Equal(rotated, await CodeOf(retry));
        Assert.Equal(rotated, await CodeOf(await client.GetAsync(Card)));
        Assert.Equal(2, (await CardsOfAsync(userId)).Count);
    }

    [Fact]
    public async Task Replaying_the_issue_after_a_rotation_returns_the_original_response_and_changes_nothing()
    {
        var (client, userId) = await LoggedInAsync();
        var issueKey = NewKey();
        var original = await CodeOf(await PostAsync(client, Card, issueKey));
        var current = await CodeOf(await PostAsync(client, Rotate, NewKey()));

        var replay = await PostAsync(client, Card, issueKey);

        Assert.True(IsReplayed(replay));
        Assert.Equal(original, await CodeOf(replay));
        Assert.Equal(current, await CodeOf(await client.GetAsync(Card)));
        Assert.Equal(2, (await CardsOfAsync(userId)).Count);
    }

    [Fact]
    public async Task Concurrent_rotations_with_different_keys_leave_exactly_one_active_card()
    {
        var (client, userId) = await LoggedInAsync();
        await PostAsync(client, Card, NewKey());

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostAsync(client, Rotate, NewKey())));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var codes = await Task.WhenAll(responses.Select(CodeOf));
        Assert.Equal(5, codes.Distinct().Count());
        var cards = await CardsOfAsync(userId);
        Assert.Equal(6, cards.Count);
        var active = Assert.Single(cards, card => card.RevokedAt is null);
        Assert.Contains(active.Code, codes);
    }

    [Fact]
    public async Task Concurrent_rotations_with_one_key_rotate_once()
    {
        var (client, userId) = await LoggedInAsync();
        await PostAsync(client, Card, NewKey());
        var key = NewKey();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => PostAsync(client, Rotate, key)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Single((await Task.WhenAll(responses.Select(CodeOf))).Distinct());
        Assert.Equal(2, (await CardsOfAsync(userId)).Count);
    }

    [Fact]
    public async Task Rotation_without_a_card_is_a_stored_404()
    {
        var (client, userId) = await LoggedInAsync();
        var key = NewKey();

        var first = await PostAsync(client, Rotate, key);
        var retry = await PostAsync(client, Rotate, key);

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(LoyaltyErrorCodes.CardMissing, await ErrorOf(first));
        Assert.True(IsReplayed(retry));
        Assert.Empty(await CardsOfAsync(userId));
    }

    [Fact]
    public async Task Cards_are_isolated_between_accounts()
    {
        var (anna, annaId) = await LoggedInAsync();
        var (bartek, bartekId) = await LoggedInAsync();
        var sharedKey = NewKey();

        var annaCode = await CodeOf(await PostAsync(anna, Card, sharedKey));
        var bartekIssue = await PostAsync(bartek, Card, sharedKey);
        Assert.False(IsReplayed(bartekIssue));
        var bartekCode = await CodeOf(bartekIssue);
        Assert.NotEqual(annaCode, bartekCode);

        var bartekRotated = await CodeOf(await PostAsync(bartek, Rotate, NewKey()));

        Assert.Equal(annaCode, await CodeOf(await anna.GetAsync(Card)));
        Assert.Equal(bartekRotated, await CodeOf(await bartek.GetAsync(Card)));
        Assert.Single(await CardsOfAsync(annaId));
        Assert.Equal(2, (await CardsOfAsync(bartekId)).Count);
    }

    [Theory]
    [InlineData(Card)]
    [InlineData(Rotate)]
    public async Task Mutations_require_csrf_and_an_idempotency_key(string url)
    {
        var (client, userId) = await LoggedInAsync();
        if (url == Rotate)
            await PostAsync(client, Card, NewKey());

        var withoutCsrf = await PostAsync(client, url, NewKey(), csrf: false);
        var withoutKey = await PostAsync(client, url, key: null);

        Assert.Equal(HttpStatusCode.Forbidden, withoutCsrf.StatusCode);
        Assert.Equal("csrf.invalid", await ErrorOf(withoutCsrf));
        Assert.Equal(HttpStatusCode.BadRequest, withoutKey.StatusCode);
        Assert.Equal(IdempotencyErrorCodes.KeyRequired, await ErrorOf(withoutKey));
        Assert.Equal(url == Rotate ? 1 : 0, (await CardsOfAsync(userId)).Count);
    }

    [Theory]
    [InlineData(RoleNames.Cashier)]
    [InlineData(RoleNames.Admin)]
    public async Task Staff_only_accounts_have_no_card_endpoints(string role)
    {
        var (client, userId) = await LoggedInAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Card)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(client, Card, NewKey())).StatusCode);
        Assert.Empty(await CardsOfAsync(userId));
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected()
    {
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Card)).StatusCode);
    }

    [Fact]
    public async Task Database_allows_one_active_card_per_user_and_unique_codes()
    {
        var (client, userId) = await LoggedInAsync();
        var code = await CodeOf(await PostAsync(client, Card, NewKey()));

        var secondActive = await Assert.ThrowsAsync<PostgresException>(
            () => InsertCardAsync(userId, LoyaltyCardCode.Generate(), revoked: false));
        Assert.Equal("UX_LoyaltyCards_ActivePerUser", secondActive.ConstraintName);

        var duplicateCode = await Assert.ThrowsAsync<PostgresException>(() => InsertCardAsync(userId, code, revoked: true));
        Assert.Equal("UX_LoyaltyCards_Code", duplicateCode.ConstraintName);

        var badFormat = await Assert.ThrowsAsync<PostgresException>(
            () => InsertCardAsync(userId, "8" + new string('0', 25), revoked: true));
        Assert.Equal("CK_LoyaltyCards_Code_Format", badFormat.ConstraintName);

        await InsertCardAsync(userId, LoyaltyCardCode.Generate(), revoked: true);
    }

    private async Task InsertCardAsync(string userId, string code, bool revoked)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO "LoyaltyCards" ("Id", "UserId", "Code", "IssuedAt", "RevokedAt")
            VALUES ({Guid.NewGuid()}, {userId}, {code}, now(), {(revoked ? DateTimeOffset.UtcNow.AddMinutes(1) : null)})
            """);
    }
}

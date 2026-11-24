using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Notifications;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

/// <summary>
/// The loyalty hub (ADR 0021): "pointsAwarded" reaches only the purchase's customer, once, after the
/// purchase committed; foreign origins and other roles are refused.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LoyaltyHubTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(700);

    private readonly List<string> _emails = [];
    private readonly List<HubConnection> _connections = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
            await connection.DisposeAsync();
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
    }

    private sealed record Account(HttpClient Client, CookieContainerHandler Cookies);

    private async Task<Account> AccountAsync(string role = RoleNames.User)
    {
        var email = $"loyalty-hub-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, role);
        var cookies = new CookieContainerHandler();
        var client = factory.CreateDefaultClient(cookies);
        await client.LoginAsync(email, Password);
        return new Account(client, cookies);
    }

    private async Task<HttpClient> CashierAsync()
    {
        var email = $"loyalty-hub-cashier-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        return await factory.CreateCashierAsync(email);
    }

    private HubConnection Connection(Account account, string origin = ApiFactory.SupportHubOrigin)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, LoyaltyHub.Path),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => new CookieContainerHandler(
                        account.Cookies.Container
                    )
                    {
                        InnerHandler = factory.Server.CreateHandler(),
                    };
                    options.Headers["Origin"] = origin;
                }
            )
            .Build();
        _connections.Add(connection);
        return connection;
    }

    private async Task<ConcurrentQueue<JsonElement>> ListenAsync(Account account)
    {
        var events = new ConcurrentQueue<JsonElement>();
        var connection = Connection(account);
        connection.On<JsonElement>("pointsAwarded", data => events.Enqueue(data));
        await connection.StartAsync();
        return events;
    }

    private static async Task<JsonElement> NextAsync(ConcurrentQueue<JsonElement> events)
    {
        var deadline = DateTime.UtcNow + Wait;
        while (DateTime.UtcNow < deadline)
        {
            if (events.TryDequeue(out var data))
                return data;
            await Task.Delay(20);
        }
        throw new TimeoutException("No pointsAwarded event.");
    }

    [Fact]
    public async Task Points_awarded_reaches_only_the_customer_once()
    {
        var customer = await AccountAsync();
        var other = await AccountAsync();
        var card = await customer.Client.IssueCardAsync();
        var mine = await ListenAsync(customer);
        var theirs = await ListenAsync(other);
        var cashier = await CashierAsync();

        var key = Guid.NewGuid().ToString();
        var sale = CashierTestHelpers.Sale(card, null, amount: 123.99m, liters: 40.5m);
        CashierTestHelpers.AssertCreated(await cashier.PurchaseAsync(sale, key));

        var awarded = await NextAsync(mine);
        Assert.Equal(123, awarded.GetProperty("points").GetInt32());
        Assert.Equal(123.99m, awarded.GetProperty("grossAmount").GetDecimal());
        Assert.Equal(123.99m, awarded.GetProperty("paidAmount").GetDecimal());
        Assert.Equal(0m, awarded.GetProperty("discount").GetDecimal());
        Assert.Equal(40.5m, awarded.GetProperty("liters").GetDecimal());
        Assert.Equal("ST-01", awarded.GetProperty("stationCode").GetString());
        // Nothing about the customer's identity or the card in the event.
        Assert.False(awarded.TryGetProperty("cardCode", out _));

        // A replay (lost response) does not run the purchase again: no second event.
        CashierTestHelpers.AssertCreated(await cashier.PurchaseAsync(sale, key));
        // A rejected purchase (receipt already registered) sends nothing.
        var duplicate = await cashier.PurchaseAsync(sale);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await Task.Delay(Quiet);
        Assert.Empty(mine);
        Assert.Empty(theirs);
    }

    [Fact]
    public async Task Hub_refuses_foreign_origins_and_other_roles()
    {
        var customer = await AccountAsync();
        var foreign = Connection(customer, "https://evil.example");
        var refused = await Assert.ThrowsAsync<HttpRequestException>(() => foreign.StartAsync());
        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var support = await AccountAsync(RoleNames.Support);
        var wrongRole = Connection(support);
        var denied = await Assert.ThrowsAsync<HttpRequestException>(() => wrongRole.StartAsync());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // The code names the hub: the guard is shared with the support hub.
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{LoyaltyHub.Path}/negotiate?negotiateVersion=1"
        );
        request.Headers.Add("Origin", "https://evil.example");
        var response = await customer.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("loyalty.origin_not_allowed", problem.GetProperty("code").GetString());
    }
}

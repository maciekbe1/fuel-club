using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Support;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing.Handlers;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

/// <summary>
/// The support hub (ADR 0014): notifications and "typing" reach only the case owner and the
/// current assignee; a former assignee drops out at once; foreign origins and other roles are refused.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SupportHubTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Cases = "/api/support/cases";
    private const string Agent = "/api/support/agent/cases";
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    /// <summary>How long a test waits to be sure an event did not arrive (after a control event did).</summary>
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

    private sealed record Account(HttpClient Client, string UserId, CookieContainerHandler Cookies);

    private sealed record CaseBody(Guid Id);

    private sealed record HubEvent(string Name, JsonElement Data);

    /// <summary>A hub connection that records every event it receives.</summary>
    private sealed class Listener(HubConnection connection)
    {
        public readonly ConcurrentQueue<HubEvent> Events = new();
        public HubConnection Connection => connection;

        public async Task<HubEvent> NextAsync(string name, Func<JsonElement, bool>? match = null)
        {
            var deadline = DateTime.UtcNow + Wait;
            while (DateTime.UtcNow < deadline)
            {
                var hit = Events.FirstOrDefault(e =>
                    e.Name == name && (match?.Invoke(e.Data) ?? true)
                );
                if (hit is not null)
                    return hit;
                await Task.Delay(20);
            }
            throw new TimeoutException($"No {name} event.");
        }

        public bool Got(string name) => Events.Any(e => e.Name == name);

        public void Clear() => Events.Clear();
    }

    private async Task<Account> AccountAsync(string role = RoleNames.User)
    {
        var email = $"support-hub-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, role);
        // One cookie jar shared by the HTTP client and the hub connection of this account.
        var cookies = new CookieContainerHandler();
        var client = factory.CreateDefaultClient(cookies);
        await client.LoginAsync(email, Password);
        await using var scope = factory.Services.CreateAsyncScope();
        var userId = await scope
            .ServiceProvider.GetRequiredService<AppDbContext>()
            .Users.Where(u => u.Email == email)
            .Select(u => u.Id)
            .SingleAsync();
        return new Account(client, userId, cookies);
    }

    /// <summary>
    /// Like a same-origin browser: Origin on the negotiation POST only, none on the long-polling
    /// requests that follow.
    /// </summary>
    private sealed class BrowserLikeOrigin(string origin) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            request.Headers.Remove("Origin");
            if (request.RequestUri!.AbsolutePath.EndsWith("/negotiate", StringComparison.Ordinal))
                request.Headers.Add("Origin", origin);
            return base.SendAsync(request, cancellationToken);
        }
    }

    private HubConnection Connection(
        Account? account,
        string? origin = ApiFactory.SupportHubOrigin,
        bool browserLike = false
    ) =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, SupportHub.Path),
                options =>
                {
                    options.Transports = HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ =>
                    {
                        HttpMessageHandler handler = factory.Server.CreateHandler();
                        if (browserLike)
                            handler = new BrowserLikeOrigin(origin!) { InnerHandler = handler };
                        if (account is null)
                            return handler;
                        return new CookieContainerHandler(account.Cookies.Container)
                        {
                            InnerHandler = handler,
                        };
                    };
                    if (origin is not null && !browserLike)
                        options.Headers["Origin"] = origin;
                }
            )
            .Build();

    private async Task<Listener> ListenAsync(Account account, bool browserLike = false)
    {
        var connection = Connection(account, browserLike: browserLike);
        _connections.Add(connection);
        var listener = new Listener(connection);
        foreach (var name in new[] { "messageAdded", "caseChanged", "queueChanged", "typing" })
            connection.On<JsonElement>(
                name,
                data => listener.Events.Enqueue(new HubEvent(name, data))
            );
        await connection.StartAsync();
        return listener;
    }

    private static async Task<HttpResponseMessage> PostAsync(
        Account account,
        string url,
        object? body = null,
        bool key = false
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        if (key)
            request.Headers.Add(Idempotency.HeaderName, Guid.NewGuid().ToString());
        request.Headers.Add(
            TestHttpExtensions.CsrfHeader,
            await account.Client.GetCsrfTokenAsync()
        );
        return await account.Client.SendAsync(request);
    }

    private static async Task<Guid> OpenCaseAsync(Account customer)
    {
        var created = await PostAsync(customer, Cases, new { message = "Pomocy" }, key: true);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        return (await created.Content.ReadFromJsonAsync<CaseBody>())!.Id;
    }

    private static bool Is(JsonElement data, Guid caseId) =>
        data.GetProperty("caseId").GetGuid() == caseId;

    [Fact]
    public async Task Owner_and_assignee_get_messages_state_changes_and_each_others_typing()
    {
        var customer = await AccountAsync();
        var agent = await AccountAsync(RoleNames.Support);
        var forCustomer = await ListenAsync(customer);
        var forAgent = await ListenAsync(agent);

        var caseId = await OpenCaseAsync(customer);
        await forAgent.NextAsync("queueChanged");

        await PostAsync(agent, $"{Agent}/{caseId}/claim");
        var claimed = await forCustomer.NextAsync("caseChanged", d => Is(d, caseId));
        Assert.Equal("Assigned", claimed.Data.GetProperty("status").GetString());

        var sent = await PostAsync(
            agent,
            $"{Agent}/{caseId}/messages",
            new { text = "Dzień dobry" },
            key: true
        );
        var seq = (await sent.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("seq")
            .GetInt32();
        var added = await forCustomer.NextAsync("messageAdded", d => Is(d, caseId));
        Assert.Equal(seq, added.Data.GetProperty("seq").GetInt32());
        // Only a notification: the text is read through the cursor.
        Assert.DoesNotContain("Dzień dobry", added.Data.ToString());
        await forAgent.NextAsync("messageAdded", d => Is(d, caseId));

        await forCustomer.Connection.InvokeAsync("Typing", caseId);
        var typing = await forAgent.NextAsync("typing", d => Is(d, caseId));
        Assert.Equal("Customer", typing.Data.GetProperty("author").GetString());
        await forAgent.Connection.InvokeAsync("Typing", caseId);
        Assert.Equal(
            "Support",
            (await forCustomer.NextAsync("typing", d => Is(d, caseId)))
                .Data.GetProperty("author")
                .GetString()
        );
    }

    [Fact]
    public async Task After_a_release_the_former_assignee_gets_nothing_and_its_typing_goes_nowhere()
    {
        var customer = await AccountAsync();
        var first = await AccountAsync(RoleNames.Support);
        var second = await AccountAsync(RoleNames.Support);
        var forCustomer = await ListenAsync(customer);
        var forFirst = await ListenAsync(first);
        var forSecond = await ListenAsync(second);
        var caseId = await OpenCaseAsync(customer);
        await PostAsync(first, $"{Agent}/{caseId}/claim");
        await forCustomer.NextAsync("caseChanged", d => Is(d, caseId));

        // The former assignee did write in this case: recipients still come from the current state.
        Assert.Equal(
            HttpStatusCode.Created,
            (
                await PostAsync(
                    first,
                    $"{Agent}/{caseId}/messages",
                    new { text = "Sprawdzam" },
                    key: true
                )
            ).StatusCode
        );
        await PostAsync(first, $"{Agent}/{caseId}/release");
        // Told once that the case left them.
        var left = await forFirst.NextAsync(
            "caseChanged",
            d => Is(d, caseId) && d.GetProperty("status").GetString() == "Waiting"
        );
        Assert.NotNull(left);
        await PostAsync(second, $"{Agent}/{caseId}/claim");
        await forCustomer.NextAsync(
            "caseChanged",
            d => Is(d, caseId) && d.GetProperty("status").GetString() == "Assigned"
        );
        forFirst.Clear();
        forCustomer.Clear();

        await PostAsync(customer, $"{Cases}/{caseId}/messages", new { text = "Halo" }, key: true);
        await forSecond.NextAsync("messageAdded", d => Is(d, caseId));
        await forFirst.Connection.InvokeAsync("Typing", caseId);
        await forCustomer.Connection.InvokeAsync("Typing", caseId);
        await forSecond.NextAsync("typing", d => Is(d, caseId));
        await Task.Delay(Quiet);

        Assert.False(forFirst.Got("messageAdded"));
        Assert.False(forFirst.Got("typing"));
        Assert.False(forFirst.Got("caseChanged"));
        Assert.False(forCustomer.Got("typing"));
    }

    [Fact]
    public async Task Strangers_typing_reaches_nobody_and_typing_is_throttled()
    {
        var customer = await AccountAsync();
        var agent = await AccountAsync(RoleNames.Support);
        var stranger = await AccountAsync();
        var otherAgent = await AccountAsync(RoleNames.Support);
        var forCustomer = await ListenAsync(customer);
        var forAgent = await ListenAsync(agent);
        var forStranger = await ListenAsync(stranger);
        var forOtherAgent = await ListenAsync(otherAgent);
        var caseId = await OpenCaseAsync(customer);
        await PostAsync(agent, $"{Agent}/{caseId}/claim");
        await forCustomer.NextAsync("caseChanged", d => Is(d, caseId));
        forCustomer.Clear();
        forAgent.Clear();

        await forStranger.Connection.InvokeAsync("Typing", caseId);
        await forOtherAgent.Connection.InvokeAsync("Typing", caseId);
        await forCustomer.Connection.InvokeAsync("Typing", caseId);
        await forCustomer.Connection.InvokeAsync("Typing", caseId);
        await forAgent.NextAsync("typing", d => Is(d, caseId));
        await Task.Delay(Quiet);

        Assert.False(forCustomer.Got("typing"));
        Assert.False(forStranger.Got("typing"));
        // Two signals within a second: one forwarded.
        Assert.Single(forAgent.Events, e => e.Name == "typing");
        // Nobody else's case events reach the stranger or the other agent.
        Assert.False(forStranger.Got("caseChanged") || forOtherAgent.Got("caseChanged"));
    }

    [Fact]
    public async Task The_hub_refuses_foreign_origins_anonymous_callers_and_other_roles()
    {
        var customer = await AccountAsync();
        var cashier = await AccountAsync(RoleNames.Cashier);
        var admin = await AccountAsync(RoleNames.Admin);

        async Task<int?> StatusOf(HubConnection connection)
        {
            await using (connection)
            {
                var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
                    connection.StartAsync()
                );
                return (int?)error.StatusCode;
            }
        }

        Assert.Equal(403, await StatusOf(Connection(customer, "https://evil.example")));
        Assert.Equal(403, await StatusOf(Connection(customer, origin: null)));
        Assert.Equal(401, await StatusOf(Connection(null)));
        Assert.Equal(403, await StatusOf(Connection(cashier)));
        Assert.Equal(403, await StatusOf(Connection(admin)));
        // The allowed origin with a customer session connects.
        await ListenAsync(customer);
    }

    [Fact]
    public async Task Long_polling_without_origin_after_a_guarded_negotiation_works_like_a_browser()
    {
        var customer = await AccountAsync();
        var agent = await AccountAsync(RoleNames.Support);
        var forCustomer = await ListenAsync(customer, browserLike: true);
        var caseId = await OpenCaseAsync(customer);

        await PostAsync(agent, $"{Agent}/{caseId}/claim");

        Assert.Equal(
            "Assigned",
            (await forCustomer.NextAsync("caseChanged", d => Is(d, caseId)))
                .Data.GetProperty("status")
                .GetString()
        );
    }

    [Fact]
    public async Task An_agent_switching_between_two_cases_keeps_both_typing_signals()
    {
        var first = await AccountAsync();
        var second = await AccountAsync();
        var agent = await AccountAsync(RoleNames.Support);
        var forFirst = await ListenAsync(first);
        var forSecond = await ListenAsync(second);
        var forAgent = await ListenAsync(agent);
        var firstCase = await OpenCaseAsync(first);
        var secondCase = await OpenCaseAsync(second);
        await PostAsync(agent, $"{Agent}/{firstCase}/claim");
        await PostAsync(agent, $"{Agent}/{secondCase}/claim");

        await forAgent.Connection.InvokeAsync("Typing", firstCase);
        await forAgent.Connection.InvokeAsync("Typing", secondCase);

        await forFirst.NextAsync("typing", d => Is(d, firstCase));
        await forSecond.NextAsync("typing", d => Is(d, secondCase));
    }

    [Fact]
    public void Hub_origins_must_be_bare_scheme_host_and_port()
    {
        var validator = new SupportOptionsValidator();
        Assert.True(
            validator
                .Validate(
                    null,
                    new SupportOptions
                    {
                        HubAllowedOrigins = ["https://app.example", "http://localhost:3000"],
                    }
                )
                .Succeeded
        );
        foreach (
            var bad in new[]
            {
                "https://app.example/",
                "https://app.example/chat",
                "app.example",
                "ftp://app.example",
            }
        )
            Assert.True(
                validator.Validate(null, new SupportOptions { HubAllowedOrigins = [bad] }).Failed,
                bad
            );
    }
}

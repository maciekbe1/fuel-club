using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant.Model;
using FuelClub.Api.Modules.Assistant.Tools;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

/// <summary>
/// A chat turn already streaming when the customer hands the conversation over (ADR 0012): the
/// late answer and a late activation proposal never reach the client as a live bot conversation.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SupportHandoffTurnTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private readonly List<string> _emails = [];
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
        await DbAsync(db => db.RewardOffers.Where(o => o.Name.EndsWith(_tag)).ExecuteDeleteAsync());
    }

    private sealed record SseEvent(string Name, JsonElement Data);

    /// <summary>The model's first step waits until the test has opened a case, then returns <c>step</c>.</summary>
    private sealed class PausedModel(Func<ChatModelContext, ModelStep> first) : IChatModel
    {
        public readonly TaskCompletionSource Entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        public readonly TaskCompletionSource Release = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _steps;

        public string Provider => "test";
        public string Model => "paused";
        public string PromptVersion => "test-v1";
        public bool Demo => false;

        public async Task<ModelStep> NextAsync(ChatModelContext context, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _steps) > 1)
                return new ModelStep.Reply("Gotowe.");
            Entered.TrySetResult();
            await Release.Task.WaitAsync(ct);
            return first(context);
        }
    }

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<(HttpClient Client, string UserId)> CustomerAsync(
        WebApplicationFactory<Program> host
    )
    {
        var email = $"support-turn-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, RoleNames.User);
        var client = host.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        var userId = await DbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()
        );
        return (client, userId);
    }

    private static async Task<List<SseEvent>> ChatAsync(HttpClient client, string message)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/chat")
        {
            Content = JsonContent.Create(new { message }),
        };
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = new List<SseEvent>();
        foreach (
            var frame in (await response.Content.ReadAsStringAsync()).Split(
                "\n\n",
                StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            string? name = null;
            string? data = null;
            foreach (var line in frame.Split('\n'))
                if (line.StartsWith("event: ", StringComparison.Ordinal))
                    name = line["event: ".Length..];
                else if (line.StartsWith("data: ", StringComparison.Ordinal))
                    data = line["data: ".Length..];
            if (name is not null && data is not null)
                events.Add(new SseEvent(name, JsonDocument.Parse(data).RootElement.Clone()));
        }
        return events;
    }

    private static async Task OpenCaseAsync(HttpClient client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/support/cases")
        {
            Content = JsonContent.Create(new { message = "Poproszę człowieka" }),
        };
        request.Headers.Add(Idempotency.HeaderName, Guid.NewGuid().ToString());
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        Assert.Equal(HttpStatusCode.Created, (await client.SendAsync(request)).StatusCode);
    }

    private static void AssertStoppedByHandoff(List<SseEvent> events)
    {
        Assert.Equal(["meta", "error"], events.Select(e => e.Name));
        Assert.Equal("assistant.handoff_active", events[^1].Data.GetProperty("code").GetString());
    }

    [Fact]
    public async Task A_late_answer_of_a_turn_started_before_the_handoff_is_not_streamed()
    {
        var model = new PausedModel(_ => new ModelStep.Reply("Masz 100 pkt."));
        await using var host = Host(model);
        var (client, _) = await CustomerAsync(host);

        var turn = ChatAsync(client, "Ile mam punktów?");
        await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await OpenCaseAsync(client);
        model.Release.SetResult();

        var events = await turn;
        AssertStoppedByHandoff(events);
        // The customer's choice, not a model failure: no Error turn for the review queue.
        Assert.False(
            await DbAsync(db =>
                db.AssistantTurns.AnyAsync(t =>
                    t.ErrorCode == "assistant.handoff_active"
                    && t.Outcome != FuelClub.Api.Modules.Assistant.Tracing.TurnOutcome.Cancelled
                )
            )
        );
        Assert.DoesNotContain("100 pkt", string.Join("", events.Select(e => e.Data.ToString())));
    }

    [Fact]
    public async Task A_late_activation_proposal_is_neither_streamed_nor_usable()
    {
        Guid couponId = Guid.Empty;
        var model = new PausedModel(_ => new ModelStep.Tool(
            new ToolCall(
                AssistantTools.ProposeCouponActivation,
                JsonSerializer.SerializeToElement(new { couponId })
            )
        ));
        await using var host = Host(model);
        var (client, userId) = await CustomerAsync(host);
        couponId = await PurchasedCouponAsync(userId);

        var turn = ChatAsync(client, "Aktywuj kupon");
        await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await OpenCaseAsync(client);
        model.Release.SetResult();

        var events = await turn;
        AssertStoppedByHandoff(events);
        Assert.DoesNotContain(events, e => e.Name is "tool" or "confirmation" or "delta");
        Assert.Equal(
            CouponStatus.Purchased,
            await DbAsync(db =>
                db.Coupons.Where(c => c.Id == couponId).Select(c => c.Status).SingleAsync()
            )
        );
    }

    [Fact]
    public async Task A_turn_without_a_handoff_still_answers()
    {
        var model = new PausedModel(_ => new ModelStep.Reply("Masz 100 pkt."));
        await using var host = Host(model);
        var (client, _) = await CustomerAsync(host);

        var turn = ChatAsync(client, "Ile mam punktów?");
        await model.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        model.Release.SetResult();

        Assert.Equal(["meta", "delta", "done"], (await turn).Select(e => e.Name));
    }

    [Fact]
    public async Task The_model_only_proposes_a_handoff_and_nothing_is_handed_over()
    {
        var (client, userId) = await CustomerAsync(factory);

        var events = await ChatAsync(client, "Chcę porozmawiać z konsultantem");

        Assert.Equal(["meta", "tool", "handoff", "delta", "done"], events.Select(e => e.Name));
        Assert.Equal(
            (AssistantTools.ProposeHandoff, "ok"),
            (
                events[1].Data.GetProperty("name").GetString(),
                events[1].Data.GetProperty("status").GetString()
            )
        );
        Assert.Contains(
            "Porozmawiaj z konsultantem",
            events[3].Data.GetProperty("text").GetString()
        );
        // No case: the button with the consent opens it, not the model.
        Assert.False(await DbAsync(db => db.SupportCases.AnyAsync(c => c.UserId == userId)));
    }

    [Fact]
    public async Task A_handoff_proposal_with_arguments_is_rejected_without_the_event()
    {
        var model = new PausedModel(_ => new ModelStep.Tool(
            new ToolCall(
                AssistantTools.ProposeHandoff,
                JsonSerializer.SerializeToElement(new { userId = "someone" })
            )
        ));
        model.Release.SetResult();
        await using var host = Host(model);
        var (client, _) = await CustomerAsync(host);

        var events = await ChatAsync(client, "konsultant");

        Assert.Equal(["meta", "tool", "delta", "done"], events.Select(e => e.Name));
        Assert.Equal("rejected", events[1].Data.GetProperty("status").GetString());
    }

    private WebApplicationFactory<Program> Host(IChatModel model) =>
        factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IChatModel>(model))
        );

    private async Task<Guid> PurchasedCouponAsync(string userId)
    {
        var offer = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = $"Rabat {_tag}",
            Description = "",
            PointsCost = 100,
            DiscountPerLiter = 0.20m,
            MaxLiters = 50m,
            IsActive = true,
        };
        var coupon = new Coupon
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OfferId = offer.Id,
            OfferName = offer.Name,
            PointsCost = offer.PointsCost,
            Kind = RewardKind.FuelDiscount,
            DiscountPerLiter = offer.DiscountPerLiter,
            MaxLiters = offer.MaxLiters,
            Status = CouponStatus.Purchased,
        };
        await DbAsync(async db =>
        {
            db.RewardOffers.Add(offer);
            db.Coupons.Add(coupon);
            return await db.SaveChangesAsync();
        });
        return coupon.Id;
    }
}

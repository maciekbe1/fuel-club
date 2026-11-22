using System.Net;
using System.Net.Http.Json;
using System.Text;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class RewardOfferTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Catalog = "/api/rewards";
    private const string Admin = "/api/admin/rewards";
    private readonly List<string> _emails = [];
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
        await DbAsync(db =>
            db.RewardOffers.Where(offer => offer.Name.Contains(_tag)).ExecuteDeleteAsync()
        );
    }

    private sealed record Offer(
        Guid Id,
        string Name,
        string Description,
        int PointsCost,
        decimal? DiscountPerLiter,
        decimal? MaxLiters,
        bool IsActive,
        int Version,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt
    );

    private sealed record CatalogItem(
        Guid Id,
        string Name,
        string Description,
        int PointsCost,
        decimal? DiscountPerLiter,
        decimal? MaxLiters
    );

    private sealed record ListBody<T>(List<T> Items);

    private sealed record ProblemBody(string? Code, Dictionary<string, FieldError[]>? Errors);

    private sealed record FieldError(string Field, string Code);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<HttpClient> AccountAsync(string role)
    {
        var email = $"rewards-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, role);
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        return client;
    }

    private string Name(string label) => $"{label} {_tag}";

    private object Form(
        string label = "Rabat",
        int cost = 500,
        decimal rate = 0.20m,
        decimal liters = 50m,
        bool? active = true,
        string description = "Na dowolne paliwo"
    ) =>
        new
        {
            name = Name(label),
            description,
            pointsCost = cost,
            discountPerLiter = rate,
            maxLiters = liters,
            isActive = active,
        };

    private static object WithVersion(object form, int? version)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToNode(form)!.AsObject();
        json["version"] = version;
        return json;
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        object? body,
        string? key = null,
        bool csrf = true
    )
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        if (key is not null)
            request.Headers.Add(Idempotency.HeaderName, key);
        if (csrf)
            request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private async Task<Offer> CreateAsync(HttpClient admin, object form)
    {
        var response = await SendAsync(admin, HttpMethod.Post, Admin, form, NewKey());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Offer>())!;
    }

    private static Task<HttpResponseMessage> PutAsync(
        HttpClient admin,
        Guid id,
        object form,
        int? version
    ) => SendAsync(admin, HttpMethod.Put, $"{Admin}/{id}", WithVersion(form, version));

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode status,
        string code
    )
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ProblemBody>())!.Code);
    }

    private Task<RewardOffer> StoredAsync(Guid id) =>
        DbAsync(db => db.RewardOffers.AsNoTracking().SingleAsync(o => o.Id == id));

    [Fact]
    public async Task Catalog_lists_active_offers_by_cost_then_name_then_id()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var b = await CreateAsync(admin, Form("B", cost: 100));
        var a = await CreateAsync(admin, Form("A", cost: 100));
        var cheap = await CreateAsync(admin, Form("Z", cost: 50));
        var twin1 = await CreateAsync(admin, Form("T", cost: 300));
        var twin2 = await CreateAsync(admin, Form("T", cost: 300));
        var inactive = await CreateAsync(admin, Form("Off", cost: 1, active: false));

        var customer = await AccountAsync(RoleNames.User);
        var catalog = (await customer.GetFromJsonAsync<ListBody<CatalogItem>>(Catalog))!
            .Items.Where(item => item.Name.Contains(_tag))
            .ToList();

        var expected = new[] { cheap, a, b }
            .Concat(new[] { twin1, twin2 }.OrderBy(o => o.Id))
            .Select(o => o.Id);
        Assert.Equal(expected, catalog.Select(item => item.Id));
        Assert.DoesNotContain(catalog, item => item.Id == inactive.Id);
        Assert.Equal(
            new CatalogItem(cheap.Id, Name("Z"), "Na dowolne paliwo", 50, 0.20m, 50m),
            catalog[0]
        );
    }

    [Fact]
    public async Task Catalog_requires_the_user_role()
    {
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync(Catalog)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await (await AccountAsync(RoleNames.Cashier)).GetAsync(Catalog)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await (await AccountAsync(RoleNames.Admin)).GetAsync(Catalog)).StatusCode
        );
    }

    [Fact]
    public async Task Admin_endpoints_require_the_admin_role()
    {
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Admin)).StatusCode);
        foreach (
            var client in new[]
            {
                await AccountAsync(RoleNames.User),
                await AccountAsync(RoleNames.Cashier),
            }
        )
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Admin)).StatusCode);
            Assert.Equal(
                HttpStatusCode.Forbidden,
                (await SendAsync(client, HttpMethod.Post, Admin, Form(), NewKey())).StatusCode
            );
        }
        Assert.Equal(
            0,
            await DbAsync(db => db.RewardOffers.CountAsync(o => o.Name.Contains(_tag)))
        );
    }

    [Fact]
    public async Task Create_returns_the_offer_with_version_one_and_location()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var response = await SendAsync(
            admin,
            HttpMethod.Post,
            Admin,
            Form(cost: 750, rate: 0.25m, liters: 40.5m),
            NewKey()
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var offer = (await response.Content.ReadFromJsonAsync<Offer>())!;
        Assert.Equal($"{Admin}/{offer.Id}", response.Headers.Location!.OriginalString);
        Assert.Equal(
            (Name("Rabat"), 750, 0.25m, 40.5m, true, 1),
            (
                offer.Name,
                offer.PointsCost,
                offer.DiscountPerLiter,
                offer.MaxLiters,
                offer.IsActive,
                offer.Version
            )
        );
        Assert.Equal(
            offer,
            await admin.GetFromJsonAsync<Offer>(response.Headers.Location.OriginalString)
        );
        Assert.Contains(
            (await admin.GetFromJsonAsync<ListBody<Offer>>(Admin))!.Items,
            o => o.Id == offer.Id
        );
    }

    [Fact]
    public async Task Create_is_idempotent()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var key = NewKey();
        var first = await SendAsync(admin, HttpMethod.Post, Admin, Form("Once"), key);
        var retry = await SendAsync(admin, HttpMethod.Post, Admin, Form("Once"), key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<Offer>())!.Id,
            (await retry.Content.ReadFromJsonAsync<Offer>())!.Id
        );
        Assert.Equal(
            1,
            await DbAsync(db => db.RewardOffers.CountAsync(o => o.Name == Name("Once")))
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await SendAsync(admin, HttpMethod.Post, Admin, Form("Once"), key: null)).StatusCode
        );
    }

    [Fact]
    public async Task Create_validates_fields_and_stores_nothing()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        // Tagged (but too long) name, so the test can prove that no row was stored.
        var response = await SendAsync(
            admin,
            HttpMethod.Post,
            Admin,
            new
            {
                name = new string('x', 101) + _tag,
                description = "",
                pointsCost = 0,
                discountPerLiter = 0.205m,
                maxLiters = 0m,
            },
            NewKey()
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<ProblemBody>())!;
        Assert.Equal("rewards.validation_failed", problem.Code);
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["name"] = "rewards.too_long",
                ["pointsCost"] = "rewards.points_cost_out_of_range",
                ["discountPerLiter"] = "rewards.discount_per_liter_precision",
                ["maxLiters"] = "rewards.max_liters_out_of_range",
                ["isActive"] = "rewards.required_field",
            },
            problem.Errors!.ToDictionary(e => e.Key, e => e.Value.Single().Code)
        );
        Assert.Equal(
            0,
            await DbAsync(db => db.RewardOffers.CountAsync(o => o.Name.Contains(_tag)))
        );
    }

    [Fact]
    public async Task Missing_numbers_are_required_not_out_of_range()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var offer = await CreateAsync(admin, Form());
        var expected = new Dictionary<string, string>
        {
            ["pointsCost"] = "rewards.required_field",
            ["discountPerLiter"] = "rewards.required_field",
            ["maxLiters"] = "rewards.required_field",
        };

        var create = await SendAsync(
            admin,
            HttpMethod.Post,
            Admin,
            new
            {
                name = Name("Missing"),
                description = "",
                isActive = true,
            },
            NewKey()
        );
        var update = await SendAsync(
            admin,
            HttpMethod.Put,
            $"{Admin}/{offer.Id}",
            new
            {
                name = Name("Missing"),
                description = "",
                isActive = true,
                version = 1,
            }
        );

        foreach (var response in new[] { create, update })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(
                expected,
                (await response.Content.ReadFromJsonAsync<ProblemBody>())!.Errors!.ToDictionary(
                    e => e.Key,
                    e => e.Value.Single().Code
                )
            );
        }
        Assert.Equal(1, (await StoredAsync(offer.Id)).Version);
    }

    [Fact]
    public async Task A_change_made_through_ef_raises_the_version_so_an_older_form_gets_409()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var offer = await CreateAsync(admin, Form());

        await DbAsync(async db =>
        {
            var tracked = await db.RewardOffers.SingleAsync(o => o.Id == offer.Id);
            tracked.IsActive = false;
            return await db.SaveChangesAsync();
        });
        var changed = await StoredAsync(offer.Id);
        Assert.Equal(2, changed.Version);
        Assert.True(changed.UpdatedAt > offer.UpdatedAt);

        await AssertProblemAsync(
            await PutAsync(admin, offer.Id, Form("Stary"), offer.Version),
            HttpStatusCode.Conflict,
            "rewards.offer_version_conflict"
        );
        Assert.False((await StoredAsync(offer.Id)).IsActive);
    }

    [Fact]
    public async Task Numbers_sent_as_strings_are_rejected()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var response = await SendAsync(
            admin,
            HttpMethod.Post,
            Admin,
            new
            {
                name = Name("Str"),
                description = "",
                pointsCost = "500",
                discountPerLiter = 0.2m,
                maxLiters = 50m,
                isActive = true,
            },
            NewKey()
        );
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Update_replaces_the_form_and_increments_the_version()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var offer = await CreateAsync(admin, Form());

        var response = await PutAsync(
            admin,
            offer.Id,
            Form("Nowa", cost: 900, rate: 0.3m, liters: 30m, active: false, description: "Opis"),
            offer.Version
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = (await response.Content.ReadFromJsonAsync<Offer>())!;
        Assert.Equal(
            (Name("Nowa"), "Opis", 900, 0.3m, 30m, false, 2),
            (
                updated.Name,
                updated.Description,
                updated.PointsCost,
                updated.DiscountPerLiter,
                updated.MaxLiters,
                updated.IsActive,
                updated.Version
            )
        );
        Assert.Equal(offer.CreatedAt, updated.CreatedAt);
        Assert.True(updated.UpdatedAt >= offer.UpdatedAt);
        // The response is the stored row (UPDATE ... RETURNING).
        var stored = await StoredAsync(offer.Id);
        Assert.Equal(
            (stored.Name, stored.PointsCost, stored.Version, stored.IsActive),
            (updated.Name, updated.PointsCost, updated.Version, updated.IsActive)
        );
    }

    [Fact]
    public async Task A_stale_form_gets_409_and_does_not_overwrite()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var offer = await CreateAsync(admin, Form());

        var first = await PutAsync(admin, offer.Id, Form("Pierwszy", cost: 600), offer.Version);
        var stale = await PutAsync(admin, offer.Id, Form("Drugi", cost: 700), offer.Version);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        await AssertProblemAsync(stale, HttpStatusCode.Conflict, "rewards.offer_version_conflict");
        var stored = await StoredAsync(offer.Id);
        Assert.Equal((Name("Pierwszy"), 600, 2), (stored.Name, stored.PointsCost, stored.Version));
    }

    [Fact]
    public async Task Concurrent_edits_with_the_same_version_succeed_once()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var offer = await CreateAsync(admin, Form());

        var responses = await Task.WhenAll(
            Enumerable
                .Range(0, 8)
                .Select(i =>
                    PutAsync(admin, offer.Id, Form($"Edit{i}", cost: 100 + i), offer.Version)
                )
        );

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var winner = (
            await responses
                .Single(r => r.StatusCode == HttpStatusCode.OK)
                .Content.ReadFromJsonAsync<Offer>()
        )!;
        var stored = await StoredAsync(offer.Id);
        Assert.Equal(
            (winner.Name, winner.PointsCost, 2),
            (stored.Name, stored.PointsCost, stored.Version)
        );
    }

    [Fact]
    public async Task Version_is_required_and_validated_before_the_conflict_check()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var offer = await CreateAsync(admin, Form());

        async Task<Dictionary<string, string>> Errors(HttpResponseMessage response)
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ProblemBody>())!.Errors!.ToDictionary(
                e => e.Key,
                e => e.Value.Single().Code
            );
        }

        Assert.Equal(
            "rewards.required_field",
            (await Errors(await PutAsync(admin, offer.Id, Form(), null)))["version"]
        );
        Assert.Equal(
            "rewards.version_out_of_range",
            (await Errors(await PutAsync(admin, offer.Id, Form(), 0)))["version"]
        );
        // Invalid form with a stale version: 400, not 409.
        Assert.Equal(
            "rewards.points_cost_out_of_range",
            (await Errors(await PutAsync(admin, offer.Id, Form(cost: 0), 99)))["pointsCost"]
        );
        // isActive is required on PUT too: a missing field must not deactivate the offer.
        Assert.Equal(
            "rewards.required_field",
            (await Errors(await PutAsync(admin, offer.Id, Form(active: null), offer.Version)))[
                "isActive"
            ]
        );
        Assert.Equal(1, (await StoredAsync(offer.Id)).Version);
    }

    [Fact]
    public async Task Unknown_offer_is_404()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var id = Guid.NewGuid();
        await AssertProblemAsync(
            await admin.GetAsync($"{Admin}/{id}"),
            HttpStatusCode.NotFound,
            "rewards.offer_not_found"
        );
        await AssertProblemAsync(
            await PutAsync(admin, id, Form(), 1),
            HttpStatusCode.NotFound,
            "rewards.offer_not_found"
        );
    }

    [Fact]
    public async Task Deactivation_hides_the_offer_from_the_catalog_and_reactivation_restores_it()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var customer = await AccountAsync(RoleNames.User);
        var offer = await CreateAsync(admin, Form("Toggle"));

        async Task<bool> Listed() =>
            (await customer.GetFromJsonAsync<ListBody<CatalogItem>>(Catalog))!.Items.Any(i =>
                i.Id == offer.Id
            );

        Assert.True(await Listed());
        var off = (
            await (
                await PutAsync(admin, offer.Id, Form("Toggle", active: false), 1)
            ).Content.ReadFromJsonAsync<Offer>()
        )!;
        Assert.False(await Listed());
        Assert.Contains(
            (await admin.GetFromJsonAsync<ListBody<Offer>>(Admin))!.Items,
            o => o.Id == offer.Id && !o.IsActive
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await PutAsync(admin, offer.Id, Form("Toggle", active: true), off.Version)).StatusCode
        );
        Assert.True(await Listed());
    }

    [Fact]
    public async Task Editing_an_offer_does_not_change_issued_coupons()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        await AccountAsync(RoleNames.User);
        var email = _emails[^1];
        var userId = await DbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()
        );
        var offer = await CreateAsync(admin, Form(cost: 500, rate: 0.20m, liters: 50m));
        var coupon = new Coupon
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OfferId = offer.Id,
            OfferName = offer.Name,
            PointsCost = 500,
            DiscountPerLiter = 0.20m,
            MaxLiters = 50m,
            Status = CouponStatus.Purchased,
        };
        await DbAsync(async db =>
        {
            db.Coupons.Add(coupon);
            return await db.SaveChangesAsync();
        });

        Assert.Equal(
            HttpStatusCode.OK,
            (
                await PutAsync(
                    admin,
                    offer.Id,
                    Form("Zmieniona", cost: 900, rate: 0.5m, liters: 10m, active: false),
                    1
                )
            ).StatusCode
        );

        var stored = await DbAsync(db =>
            db.Coupons.AsNoTracking().SingleAsync(c => c.Id == coupon.Id)
        );
        Assert.Equal(
            (Name("Rabat"), 500, 0.20m, 50m, CouponStatus.Purchased),
            (
                stored.OfferName,
                stored.PointsCost,
                stored.DiscountPerLiter,
                stored.MaxLiters,
                stored.Status
            )
        );
    }

    [Fact]
    public async Task Mutations_require_csrf()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var offer = await CreateAsync(admin, Form());

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await SendAsync(
                    admin,
                    HttpMethod.Post,
                    Admin,
                    Form("NoCsrf"),
                    NewKey(),
                    csrf: false
                )
            ).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await SendAsync(
                    admin,
                    HttpMethod.Put,
                    $"{Admin}/{offer.Id}",
                    WithVersion(Form("NoCsrf"), 1),
                    csrf: false
                )
            ).StatusCode
        );
        Assert.Equal(1, (await StoredAsync(offer.Id)).Version);
    }
}

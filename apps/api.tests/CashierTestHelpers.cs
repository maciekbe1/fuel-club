using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

/// <summary>Real cashier and card flows for tests that only need them as a step.</summary>
public static class CashierTestHelpers
{
    public const string Purchases = "/api/cashier/fuel-purchases";
    public const string Password = "Valid-Test-Password1!";

    private sealed record CardBody(string Code);

    /// <summary>A cashier assigned to a station; the caller deletes the account by its email.</summary>
    public static async Task<HttpClient> CreateCashierAsync(
        this ApiFactory factory,
        string email,
        string station = "ST-01",
        string register = "K1"
    )
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            await CashierProvisioning.EnsureCashierAsync(
                scope.ServiceProvider,
                email,
                Password,
                "Test Cashier",
                station,
                register
            );
            await transaction.CommitAsync();
        }
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        return client;
    }

    /// <summary>Issues (or returns) the logged-in customer's card through the API.</summary>
    public static async Task<string> IssueCardAsync(this HttpClient customer)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/me/loyalty-card");
        request.Headers.Add(Idempotency.HeaderName, Guid.NewGuid().ToString());
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await customer.GetCsrfTokenAsync());
        var response = await customer.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode, $"Card issue failed: {response.StatusCode}");
        return (await response.Content.ReadFromJsonAsync<CardBody>())!.Code;
    }

    public static async Task<HttpResponseMessage> PurchaseAsync(
        this HttpClient cashier,
        object body,
        string? key = null
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Purchases)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add(Idempotency.HeaderName, key ?? Guid.NewGuid().ToString());
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await cashier.GetCsrfTokenAsync());
        return await cashier.SendAsync(request);
    }

    /// <summary>A sale today (UTC is never ahead of the station date) with a unique receipt.</summary>
    public static object Sale(
        string cardCode,
        string? couponCode,
        decimal amount = 100m,
        decimal liters = 40m,
        string? receipt = null
    ) =>
        new
        {
            cardCode,
            amount,
            liters,
            saleDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
            receiptNumber = receipt ?? NewReceipt(),
            couponCode,
        };

    public static string NewReceipt() => $"R/{Guid.NewGuid():N}".ToUpperInvariant();

    public static void AssertCreated(HttpResponseMessage response) =>
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
}

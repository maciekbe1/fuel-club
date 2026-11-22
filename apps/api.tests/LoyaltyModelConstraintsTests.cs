using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Loyalty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FuelClub.Api.Tests;

/// <summary>
/// Database constraints are the last line of defence. These tests bypass application validation
/// and write directly through EF Core to prove PostgreSQL rejects invalid state on its own.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LoyaltyModelConstraintsTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private readonly string _userEmail = $"loyalty-user-{Guid.NewGuid():N}@example.test";
    private readonly string _adminEmail = $"loyalty-admin-{Guid.NewGuid():N}@example.test";
    private string _userId = null!;
    private string _adminId = null!;

    public async Task InitializeAsync()
    {
        await factory.CreateTestUserAsync(_userEmail, Password, "User");
        await factory.CreateTestUserAsync(_adminEmail, Password, "Admin");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _userId = await db
            .Users.Where(user => user.Email == _userEmail)
            .Select(user => user.Id)
            .SingleAsync();
        _adminId = await db
            .Users.Where(user => user.Email == _adminEmail)
            .Select(user => user.Id)
            .SingleAsync();
    }

    public async Task DisposeAsync()
    {
        await factory.DeleteTestUserAsync(_userEmail);
        await factory.DeleteTestUserAsync(_adminEmail);
    }

    private FuelPurchase Purchase(
        decimal amount = 100m,
        decimal discount = 0m,
        decimal? paid = null,
        decimal liters = 15.5m,
        string station = "ST-001",
        string register = "K1",
        DateOnly? saleDate = null,
        string? receipt = null
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = _userId,
            RegisteredByUserId = _adminId,
            StationCode = station,
            RegisterCode = register,
            SaleDate = saleDate ?? new DateOnly(2026, 11, 21),
            ReceiptNumber = receipt ?? NewReceiptNumber(),
            GrossAmount = amount,
            Discount = discount,
            PaidAmount = paid ?? amount - discount,
            Liters = liters,
            PointsAwarded = (int)decimal.Floor(paid ?? amount - discount),
        };

    private async Task SaveAsync(params object[] entities)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private async Task AssertRejectedAsync(
        string sqlState,
        string constraint,
        params object[] entities
    )
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(entities));
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        Assert.Equal(constraint, postgres.ConstraintName);
    }

    [Fact]
    public async Task Accepts_amount_at_the_upper_limit() =>
        await SaveAsync(Purchase(amount: 5000m));

    [Theory]
    [InlineData("0")]
    [InlineData("5000.01")]
    public async Task Rejects_amount_outside_range(string amount) =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_FuelPurchases_GrossAmount_Range",
            Purchase(
                amount: decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)
            )
        );

    [Fact]
    public async Task Rejects_non_positive_liters() =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_FuelPurchases_Liters_Range",
            Purchase(liters: 0m)
        );

    [Fact]
    public async Task Rejects_the_same_receipt_twice()
    {
        var receipt = NewReceiptNumber();
        await SaveAsync(Purchase(receipt: receipt));

        await AssertRejectedAsync(
            PostgresErrorCodes.UniqueViolation,
            "UX_FuelPurchases_Receipt",
            Purchase(receipt: receipt)
        );
    }

    [Fact]
    public async Task Same_receipt_number_is_allowed_on_another_register_station_or_day()
    {
        var receipt = NewReceiptNumber();
        await SaveAsync(
            Purchase(receipt: receipt),
            Purchase(receipt: receipt, register: "K2"),
            Purchase(receipt: receipt, station: "ST-002"),
            Purchase(receipt: receipt, saleDate: new DateOnly(2026, 11, 22))
        );
    }

    [Fact]
    public async Task Rejects_negative_balance() =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_LoyaltyAccounts_Balance_NonNegative",
            new LoyaltyAccount { UserId = _userId, Balance = -1 }
        );

    [Fact]
    public async Task Rejects_zero_ledger_delta() =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_PointsLedgerEntries_Delta_NonZero",
            LedgerEntry(Guid.NewGuid(), delta: 0)
        );

    [Fact]
    public async Task Rejects_second_ledger_entry_for_the_same_source()
    {
        var sourceId = Guid.NewGuid();
        await SaveAsync(LedgerEntry(sourceId, delta: 10));

        await AssertRejectedAsync(
            PostgresErrorCodes.UniqueViolation,
            "UX_PointsLedgerEntries_Source",
            LedgerEntry(sourceId, delta: 10)
        );
    }

    [Fact]
    public async Task Concurrent_balance_updates_are_detected()
    {
        await SaveAsync(new LoyaltyAccount { UserId = _userId, Balance = 100 });

        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();
        var firstDb = first.ServiceProvider.GetRequiredService<AppDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstCopy = await firstDb.LoyaltyAccounts.SingleAsync(account =>
            account.UserId == _userId
        );
        var secondCopy = await secondDb.LoyaltyAccounts.SingleAsync(account =>
            account.UserId == _userId
        );

        firstCopy.Balance -= 60;
        await firstDb.SaveChangesAsync();

        secondCopy.Balance -= 60;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());
    }

    private static string NewReceiptNumber() => Guid.NewGuid().ToString("N").ToUpperInvariant();

    [Fact]
    public async Task Accepts_liters_at_the_column_limit() =>
        await SaveAsync(Purchase(liters: FuelPurchaseRules.MaxLiters));

    [Fact]
    public async Task Rejects_liters_above_the_column_limit()
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
            SaveAsync(Purchase(liters: 10_000_000m))
        );
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(PostgresErrorCodes.NumericValueOutOfRange, postgres.SqlState);
    }

    [Fact]
    public async Task Rejects_default_sale_date() =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_FuelPurchases_SaleDate_Set",
            Purchase(saleDate: default(DateOnly))
        );

    [Theory]
    [InlineData("st-001", "K1", "A1", "CK_FuelPurchases_StationCode_Format")]
    [InlineData("ST-001 ", "K1", "A1", "CK_FuelPurchases_StationCode_Format")]
    [InlineData("ST-001", " K1", "A1", "CK_FuelPurchases_RegisterCode_Format")]
    [InlineData("ST-001", "K1", "a1", "CK_FuelPurchases_ReceiptNumber_Format")]
    [InlineData("ST-001", "K1", "A 1", "CK_FuelPurchases_ReceiptNumber_Format")]
    [InlineData("ST-001\n", "K1", "A1", "CK_FuelPurchases_StationCode_Format")]
    public async Task Rejects_non_normalized_receipt_parts(
        string station,
        string register,
        string receipt,
        string constraint
    ) =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            constraint,
            Purchase(station: station, register: register, receipt: receipt + NewReceiptNumber())
        );

    [Fact]
    public async Task Receipts_differing_only_in_whitespace_or_case_collide_after_normalization()
    {
        var number = NewReceiptNumber();
        var first = FuelPurchaseRules.Normalize(
            new FuelPurchaseInput(
                100m,
                10m,
                new ReceiptKey("ST-001", "K1", new DateOnly(2026, 11, 21), number)
            )
        );
        var second = FuelPurchaseRules.Normalize(
            new FuelPurchaseInput(
                100m,
                10m,
                new ReceiptKey(
                    "  st-001 ",
                    "k1 ",
                    new DateOnly(2026, 11, 21),
                    $" {number.ToLowerInvariant()} "
                )
            )
        );
        Assert.Empty(FuelPurchaseRules.Validate(second, DateOnly.MaxValue));

        await SaveAsync(FromInput(first));
        await AssertRejectedAsync(
            PostgresErrorCodes.UniqueViolation,
            "UX_FuelPurchases_Receipt",
            FromInput(second)
        );
    }

    private FuelPurchase FromInput(FuelPurchaseInput input) =>
        Purchase(
            amount: input.Amount,
            liters: input.Liters,
            station: input.Receipt.StationCode,
            register: input.Receipt.RegisterCode,
            saleDate: input.Receipt.SaleDate,
            receipt: input.Receipt.ReceiptNumber
        );

    [Fact]
    public async Task Accepts_a_discount_covering_the_whole_purchase_with_zero_paid_and_zero_points() =>
        await SaveAsync(Purchase(amount: 8.5m, discount: 8.5m));

    [Theory]
    [InlineData("-0.01")]
    [InlineData("100.01")]
    public async Task Rejects_discount_outside_zero_to_gross(string discount) =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_FuelPurchases_Discount_Range",
            Purchase(
                amount: 100m,
                discount: decimal.Parse(
                    discount,
                    System.Globalization.CultureInfo.InvariantCulture
                ),
                paid: 0m
            )
        );

    [Fact]
    public async Task Rejects_paid_amount_other_than_gross_minus_discount() =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_FuelPurchases_PaidAmount",
            Purchase(amount: 100m, discount: 10m, paid: 100m)
        );

    [Theory]
    [InlineData(PointsLedgerEntryType.FuelPurchase, -10)]
    [InlineData(PointsLedgerEntryType.CouponExchange, 10)]
    public async Task Ledger_delta_sign_follows_the_type(PointsLedgerEntryType type, int delta) =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_PointsLedgerEntries_Delta_SignByType",
            LedgerEntry(Guid.NewGuid(), delta, type)
        );

    [Fact]
    public async Task Accepts_a_coupon_exchange_debit() =>
        await SaveAsync(LedgerEntry(Guid.NewGuid(), -10, PointsLedgerEntryType.CouponExchange));

    private PointsLedgerEntry LedgerEntry(
        Guid sourceId,
        int delta,
        PointsLedgerEntryType type = PointsLedgerEntryType.FuelPurchase
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = _userId,
            Delta = delta,
            Type = type,
            SourceId = sourceId,
            BalanceAfter = Math.Max(delta, 0),
        };
}

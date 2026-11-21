using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LoyaltyModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FuelPurchases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    StationCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegisterCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SaleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceiptNumber = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    Liters = table.Column<decimal>(type: "numeric(10,3)", precision: 10, scale: 3, nullable: false),
                    PointsAwarded = table.Column<int>(type: "integer", nullable: false),
                    RegisteredByUserId = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FuelPurchases", x => x.Id);
                    table.CheckConstraint("CK_FuelPurchases_Amount_Range", "\"Amount\" > 0 AND \"Amount\" <= 5000");
                    table.CheckConstraint("CK_FuelPurchases_Liters_Positive", "\"Liters\" > 0");
                    table.CheckConstraint("CK_FuelPurchases_PointsAwarded_NonNegative", "\"PointsAwarded\" >= 0");
                    table.ForeignKey(
                        name: "FK_FuelPurchases_AspNetUsers_RegisteredByUserId",
                        column: x => x.RegisteredByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FuelPurchases_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LoyaltyAccounts",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Balance = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyAccounts", x => x.UserId);
                    table.CheckConstraint("CK_LoyaltyAccounts_Balance_NonNegative", "\"Balance\" >= 0");
                    table.ForeignKey(
                        name: "FK_LoyaltyAccounts_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PointsLedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Delta = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PointsLedgerEntries", x => x.Id);
                    table.CheckConstraint("CK_PointsLedgerEntries_BalanceAfter_NonNegative", "\"BalanceAfter\" >= 0");
                    table.CheckConstraint("CK_PointsLedgerEntries_Delta_NonZero", "\"Delta\" <> 0");
                    table.CheckConstraint("CK_PointsLedgerEntries_Type", "\"Type\" IN ('FuelPurchase')");
                    table.ForeignKey(
                        name: "FK_PointsLedgerEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FuelPurchases_RegisteredByUserId",
                table: "FuelPurchases",
                column: "RegisteredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_FuelPurchases_UserId",
                table: "FuelPurchases",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_FuelPurchases_Receipt",
                table: "FuelPurchases",
                columns: new[] { "StationCode", "RegisterCode", "SaleDate", "ReceiptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PointsLedgerEntries_UserId_CreatedAt",
                table: "PointsLedgerEntries",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_PointsLedgerEntries_Source",
                table: "PointsLedgerEntries",
                columns: new[] { "Type", "SourceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FuelPurchases");

            migrationBuilder.DropTable(
                name: "LoyaltyAccounts");

            migrationBuilder.DropTable(
                name: "PointsLedgerEntries");
        }
    }
}

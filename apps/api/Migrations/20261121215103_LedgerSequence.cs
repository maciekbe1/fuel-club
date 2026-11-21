using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LedgerSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PointsLedgerEntries_UserId_CreatedAt",
                table: "PointsLedgerEntries");

            // Explicit backfill instead of an implicit identity fill (which would follow physical row
            // order). Existing rows are numbered by (CreatedAt, Id): an approximation only, because
            // CreatedAt is the transaction start, not the moment the entry was written. Rows written
            // after this migration get their number on insert, after the customer lock.
            migrationBuilder.Sql("""
                ALTER TABLE "PointsLedgerEntries" ADD COLUMN "Sequence" bigint NULL;

                UPDATE "PointsLedgerEntries" AS entry
                SET "Sequence" = numbered.n
                FROM (
                    SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS n
                    FROM "PointsLedgerEntries"
                ) AS numbered
                WHERE entry."Id" = numbered."Id";

                ALTER TABLE "PointsLedgerEntries" ALTER COLUMN "Sequence" SET NOT NULL;
                ALTER TABLE "PointsLedgerEntries" ALTER COLUMN "Sequence" ADD GENERATED ALWAYS AS IDENTITY;

                SELECT setval(
                    pg_get_serial_sequence('"PointsLedgerEntries"', 'Sequence'),
                    COALESCE((SELECT max("Sequence") FROM "PointsLedgerEntries"), 0) + 1,
                    false);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PointsLedgerEntries_UserId_Sequence",
                table: "PointsLedgerEntries",
                columns: new[] { "UserId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "UX_PointsLedgerEntries_Sequence",
                table: "PointsLedgerEntries",
                column: "Sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PointsLedgerEntries_UserId_Sequence",
                table: "PointsLedgerEntries");

            migrationBuilder.DropIndex(
                name: "UX_PointsLedgerEntries_Sequence",
                table: "PointsLedgerEntries");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "PointsLedgerEntries");

            migrationBuilder.CreateIndex(
                name: "IX_PointsLedgerEntries_UserId_CreatedAt",
                table: "PointsLedgerEntries",
                columns: new[] { "UserId", "CreatedAt" });
        }
    }
}

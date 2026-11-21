using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class RewardOfferVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // xmin is a PostgreSQL system column: nothing to rename or drop. The explicit version is a
            // new column; existing offers start at version 1, and the application sets it from then on.
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "RewardOffers",
                type: "integer",
                nullable: false,
                defaultValue: 1);
            migrationBuilder.Sql("ALTER TABLE \"RewardOffers\" ALTER COLUMN \"Version\" DROP DEFAULT;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RewardOffers_Version_Positive",
                table: "RewardOffers",
                sql: "\"Version\" >= 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RewardOffers_Version_Positive",
                table: "RewardOffers");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "RewardOffers");
        }
    }
}

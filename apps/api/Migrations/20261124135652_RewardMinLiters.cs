using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class RewardMinLiters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RewardOffers_KindTerms",
                table: "RewardOffers"
            );

            migrationBuilder.DropCheckConstraint(name: "CK_Coupons_KindTerms", table: "Coupons");

            migrationBuilder.AddColumn<decimal>(
                name: "MinLiters",
                table: "RewardOffers",
                type: "numeric(7,3)",
                precision: 7,
                scale: 3,
                nullable: true
            );

            migrationBuilder.AddColumn<decimal>(
                name: "MinLiters",
                table: "Coupons",
                type: "numeric(7,3)",
                precision: 7,
                scale: 3,
                nullable: true
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_RewardOffers_KindTerms",
                table: "RewardOffers",
                sql: "(\"Kind\" = 'FuelDiscount' AND \"DiscountPerLiter\" IS NOT NULL AND \"MaxLiters\" IS NOT NULL)\nOR (\"Kind\" = 'Product' AND \"DiscountPerLiter\" IS NULL AND \"MaxLiters\" IS NULL AND \"MinLiters\" IS NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_RewardOffers_MinLiters_Range",
                table: "RewardOffers",
                sql: "\"MinLiters\" > 0 AND \"MinLiters\" <= 1000"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_KindTerms",
                table: "Coupons",
                sql: "(\"Kind\" = 'FuelDiscount' AND \"DiscountPerLiter\" IS NOT NULL AND \"MaxLiters\" IS NOT NULL)\nOR (\"Kind\" = 'Product' AND \"DiscountPerLiter\" IS NULL AND \"MaxLiters\" IS NULL AND \"MinLiters\" IS NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_MinLiters_Range",
                table: "Coupons",
                sql: "\"MinLiters\" > 0 AND \"MinLiters\" <= 1000"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RewardOffers_KindTerms",
                table: "RewardOffers"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_RewardOffers_MinLiters_Range",
                table: "RewardOffers"
            );

            migrationBuilder.DropCheckConstraint(name: "CK_Coupons_KindTerms", table: "Coupons");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Coupons_MinLiters_Range",
                table: "Coupons"
            );

            migrationBuilder.DropColumn(name: "MinLiters", table: "RewardOffers");

            migrationBuilder.DropColumn(name: "MinLiters", table: "Coupons");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RewardOffers_KindTerms",
                table: "RewardOffers",
                sql: "(\"Kind\" = 'FuelDiscount' AND \"DiscountPerLiter\" IS NOT NULL AND \"MaxLiters\" IS NOT NULL)\nOR (\"Kind\" = 'Product' AND \"DiscountPerLiter\" IS NULL AND \"MaxLiters\" IS NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_KindTerms",
                table: "Coupons",
                sql: "(\"Kind\" = 'FuelDiscount' AND \"DiscountPerLiter\" IS NOT NULL AND \"MaxLiters\" IS NOT NULL)\nOR (\"Kind\" = 'Product' AND \"DiscountPerLiter\" IS NULL AND \"MaxLiters\" IS NULL)"
            );
        }
    }
}

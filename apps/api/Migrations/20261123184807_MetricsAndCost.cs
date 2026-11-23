using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class MetricsAndCost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CostUsd",
                table: "assistant_turns",
                type: "numeric(12,6)",
                precision: 12,
                scale: 6,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "ModelSteps",
                table: "assistant_turns",
                type: "integer",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "PriceVersion",
                table: "assistant_turns",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_assistant_turns_cost",
                table: "assistant_turns",
                sql: "\"CostUsd\" IS NULL OR \"CostUsd\" >= 0"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_assistant_turns_cost",
                table: "assistant_turns"
            );

            migrationBuilder.DropColumn(name: "CostUsd", table: "assistant_turns");

            migrationBuilder.DropColumn(name: "ModelSteps", table: "assistant_turns");

            migrationBuilder.DropColumn(name: "PriceVersion", table: "assistant_turns");
        }
    }
}

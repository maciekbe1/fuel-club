using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class CashierStations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashierStations",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    StationCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RegisterCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashierStations", x => x.UserId);
                    table.CheckConstraint("CK_CashierStations_RegisterCode_Format", "\"RegisterCode\" ~ '^[A-Z0-9-]+$'");
                    table.CheckConstraint("CK_CashierStations_StationCode_Format", "\"StationCode\" ~ '^[A-Z0-9-]+$'");
                    table.ForeignKey(
                        name: "FK_CashierStations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashierStations");
        }
    }
}

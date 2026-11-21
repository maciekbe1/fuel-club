using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LoyaltyCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoyaltyCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Code = table.Column<string>(type: "character(26)", fixedLength: true, maxLength: 26, nullable: false),
                    IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoyaltyCards", x => x.Id);
                    table.CheckConstraint("CK_LoyaltyCards_Code_Format", "\"Code\" ~ '^[0-7][0-9A-HJKMNP-TV-Z]{25}$'");
                    table.CheckConstraint("CK_LoyaltyCards_RevokedAt_AfterIssue", "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"IssuedAt\"");
                    table.ForeignKey(
                        name: "FK_LoyaltyCards_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_LoyaltyCards_ActivePerUser",
                table: "LoyaltyCards",
                column: "UserId",
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_LoyaltyCards_Code",
                table: "LoyaltyCards",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoyaltyCards");
        }
    }
}

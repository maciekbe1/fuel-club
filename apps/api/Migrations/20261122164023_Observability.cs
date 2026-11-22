using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class Observability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReviewedAt",
                table: "assistant_turns",
                type: "timestamp with time zone",
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "ReviewedBy",
                table: "assistant_turns",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "security_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    Kind = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: false
                    ),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Code = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    Route = table.Column<string>(
                        type: "character varying(200)",
                        maxLength: 200,
                        nullable: false
                    ),
                    UserPseudonym = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_security_events", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_security_events_Kind_OccurredAt",
                table: "security_events",
                columns: new[] { "Kind", "OccurredAt" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_security_events_OccurredAt",
                table: "security_events",
                column: "OccurredAt"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "security_events");

            migrationBuilder.DropColumn(name: "ReviewedAt", table: "assistant_turns");

            migrationBuilder.DropColumn(name: "ReviewedBy", table: "assistant_turns");
        }
    }
}

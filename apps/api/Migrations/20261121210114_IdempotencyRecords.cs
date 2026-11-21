using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class IdempotencyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    Fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    ContentType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Location = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Body = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.UserId, x.IdempotencyKey });
                    table.CheckConstraint("CK_IdempotencyRecords_Fingerprint_Length", "octet_length(\"Fingerprint\") = 32");
                    table.CheckConstraint("CK_IdempotencyRecords_StatusCode_Range", "\"StatusCode\" BETWEEN 200 AND 499");
                    table.ForeignKey(
                        name: "FK_IdempotencyRecords_AspNetUsers_UserId",
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
                name: "IdempotencyRecords");
        }
    }
}

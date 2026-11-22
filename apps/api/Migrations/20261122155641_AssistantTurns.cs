using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class AssistantTurns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "assistant_turns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    UserPseudonym = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    Provider = table.Column<string>(
                        type: "character varying(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    Model = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    PromptVersion = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    Demo = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    FirstEventMs = table.Column<int>(type: "integer", nullable: true),
                    TotalMs = table.Column<int>(type: "integer", nullable: false),
                    Outcome = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: false
                    ),
                    ErrorCode = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: true
                    ),
                    ToolCalls = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: true),
                    OutputTokens = table.Column<int>(type: "integer", nullable: true),
                    Tools = table.Column<string>(type: "jsonb", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assistant_turns", x => x.Id);
                    table.CheckConstraint("ck_assistant_turns_tool_calls", "\"ToolCalls\" >= 0");
                    table.CheckConstraint("ck_assistant_turns_total_ms", "\"TotalMs\" >= 0");
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turns_CorrelationId",
                table: "assistant_turns",
                column: "CorrelationId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turns_StartedAt",
                table: "assistant_turns",
                column: "StartedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turns_UserPseudonym_StartedAt",
                table: "assistant_turns",
                columns: new[] { "UserPseudonym", "StartedAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "assistant_turns");
        }
    }
}

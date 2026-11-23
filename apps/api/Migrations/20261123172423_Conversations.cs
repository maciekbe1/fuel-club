using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class Conversations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConversationId",
                table: "assistant_turns",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "HandoffProposed",
                table: "assistant_turns",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.AddColumn<string>(
                name: "RunId",
                table: "assistant_turns",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true
            );

            migrationBuilder.AddColumn<bool>(
                name: "Synthetic",
                table: "assistant_turns",
                type: "boolean",
                nullable: false,
                defaultValue: false
            );

            migrationBuilder.CreateTable(
                name: "assistant_conversations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserPseudonym = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: false
                    ),
                    StartedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    LastActivityAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false
                    ),
                    EndedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    EndReason = table.Column<string>(
                        type: "character varying(20)",
                        maxLength: 20,
                        nullable: true
                    ),
                    Turns = table.Column<int>(type: "integer", nullable: false),
                    SupportCaseId = table.Column<Guid>(type: "uuid", nullable: true),
                    Synthetic = table.Column<bool>(type: "boolean", nullable: false),
                    RunId = table.Column<string>(
                        type: "character varying(40)",
                        maxLength: 40,
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assistant_conversations", x => x.Id);
                    table.CheckConstraint(
                        "ck_assistant_conversations_end",
                        "(\"EndedAt\" IS NULL) = (\"EndReason\" IS NULL)"
                    );
                    table.CheckConstraint(
                        "ck_assistant_conversations_run",
                        "\"RunId\" IS NULL OR \"Synthetic\""
                    );
                    table.CheckConstraint("ck_assistant_conversations_turns", "\"Turns\" >= 0");
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_assistant_turns_ConversationId",
                table: "assistant_turns",
                column: "ConversationId"
            );

            migrationBuilder.AddCheckConstraint(
                name: "ck_assistant_turns_run",
                table: "assistant_turns",
                sql: "\"RunId\" IS NULL OR \"Synthetic\""
            );

            migrationBuilder.CreateIndex(
                name: "IX_assistant_conversations_StartedAt",
                table: "assistant_conversations",
                column: "StartedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_assistant_conversations_SupportCaseId",
                table: "assistant_conversations",
                column: "SupportCaseId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_assistant_conversations_UserPseudonym_StartedAt",
                table: "assistant_conversations",
                columns: new[] { "UserPseudonym", "StartedAt" }
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "assistant_conversations");

            migrationBuilder.DropIndex(
                name: "IX_assistant_turns_ConversationId",
                table: "assistant_turns"
            );

            migrationBuilder.DropCheckConstraint(
                name: "ck_assistant_turns_run",
                table: "assistant_turns"
            );

            migrationBuilder.DropColumn(name: "ConversationId", table: "assistant_turns");

            migrationBuilder.DropColumn(name: "HandoffProposed", table: "assistant_turns");

            migrationBuilder.DropColumn(name: "RunId", table: "assistant_turns");

            migrationBuilder.DropColumn(name: "Synthetic", table: "assistant_turns");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportCases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    AssignedToUserId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    LastActivityAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    ClosedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    LastSeq = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportCases", x => x.Id);
                    table.CheckConstraint(
                        "CK_SupportCases_Assignee",
                        "(\"Status\" = 'Assigned' AND \"AssignedToUserId\" IS NOT NULL)\nOR (\"Status\" = 'Waiting' AND \"AssignedToUserId\" IS NULL)\nOR \"Status\" IN ('Closed', 'Expired')"
                    );
                    table.CheckConstraint(
                        "CK_SupportCases_ClosedAt",
                        "(\"Status\" IN ('Closed', 'Expired')) = (\"ClosedAt\" IS NOT NULL)"
                    );
                    table.CheckConstraint("CK_SupportCases_LastSeq", "\"LastSeq\" >= 0");
                    table.CheckConstraint(
                        "CK_SupportCases_Status",
                        "\"Status\" IN ('Waiting', 'Assigned', 'Closed', 'Expired')"
                    );
                    table.ForeignKey(
                        name: "FK_SupportCases_AspNetUsers_AssignedToUserId",
                        column: x => x.AssignedToUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_SupportCases_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "SupportMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Seq = table.Column<int>(type: "integer", nullable: false),
                    Author = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    AuthorUserId = table.Column<string>(type: "text", nullable: true),
                    ClientProvidedHistory = table.Column<bool>(type: "boolean", nullable: false),
                    HistoryRole = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: true
                    ),
                    Text = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: false
                    ),
                    CreatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "clock_timestamp()"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportMessages", x => x.Id);
                    table.CheckConstraint(
                        "CK_SupportMessages_Author",
                        "\"Author\" IN ('Customer', 'Support', 'System')"
                    );
                    table.CheckConstraint(
                        "CK_SupportMessages_AuthorUser",
                        "(\"Author\" = 'Support') = (\"AuthorUserId\" IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "CK_SupportMessages_History",
                        "(\"ClientProvidedHistory\" AND \"Author\" = 'Customer' AND \"HistoryRole\" IN ('user', 'assistant'))\nOR (NOT \"ClientProvidedHistory\" AND \"HistoryRole\" IS NULL)"
                    );
                    table.CheckConstraint("CK_SupportMessages_Seq", "\"Seq\" >= 1");
                    table.CheckConstraint(
                        "CK_SupportMessages_SystemEvent",
                        "\"Author\" <> 'System' OR \"Text\" IN ('claimed', 'released', 'idle_returned', 'closed_by_customer', 'closed_by_support', 'expired')"
                    );
                    table.CheckConstraint(
                        "CK_SupportMessages_Text",
                        "char_length(\"Text\") BETWEEN 1 AND 2000"
                    );
                    table.ForeignKey(
                        name: "FK_SupportMessages_AspNetUsers_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_SupportMessages_SupportCases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "SupportCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_AssignedToUserId",
                table: "SupportCases",
                column: "AssignedToUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_ClosedAt",
                table: "SupportCases",
                column: "ClosedAt"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SupportCases_Status_CreatedAt",
                table: "SupportCases",
                columns: new[] { "Status", "CreatedAt" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_SupportCases_OpenPerUser",
                table: "SupportCases",
                column: "UserId",
                unique: true,
                filter: "\"Status\" IN ('Waiting', 'Assigned')"
            );

            migrationBuilder.CreateIndex(
                name: "IX_SupportMessages_AuthorUserId",
                table: "SupportMessages",
                column: "AuthorUserId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_SupportMessages_CaseSeq",
                table: "SupportMessages",
                columns: new[] { "CaseId", "Seq" },
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back would delete conversations still under retention: refuse while any exist.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "SupportCases") THEN
                        RAISE EXCEPTION 'Support cases exist; remove them before rolling back SupportCases';
                    END IF;
                END $$;
                """
            );

            migrationBuilder.DropTable(name: "SupportMessages");

            migrationBuilder.DropTable(name: "SupportCases");
        }
    }
}

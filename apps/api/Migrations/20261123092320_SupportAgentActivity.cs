using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class SupportAgentActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AgentActivityAt",
                table: "SupportCases",
                type: "timestamp with time zone",
                nullable: true
            );

            // Cases assigned before this column: their last activity is the best known agent activity.
            migrationBuilder.Sql(
                """UPDATE "SupportCases" SET "AgentActivityAt" = "LastActivityAt" WHERE "Status" = 'Assigned'"""
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_SupportCases_AgentActivity",
                table: "SupportCases",
                sql: "\"Status\" <> 'Assigned' OR \"AgentActivityAt\" IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SupportCases_AgentActivity",
                table: "SupportCases"
            );

            migrationBuilder.DropColumn(name: "AgentActivityAt", table: "SupportCases");
        }
    }
}

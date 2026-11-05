using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class AddTokenUsageTotals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInTotals",
                schema: "UserData",
                table: "TokenUsages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TokenUsageDailyTotals",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    UsageType = table.Column<int>(type: "integer", nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    UserId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "text", nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheWriteTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheReadTokens = table.Column<long>(type: "bigint", nullable: false),
                    EmbeddingTokens = table.Column<long>(type: "bigint", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,10)", precision: 18, scale: 10, nullable: false),
                    OperationCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenUsageDailyTotals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TokenUsageExecutionTotals",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    FirstUsageAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "text", nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheWriteTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheReadTokens = table.Column<long>(type: "bigint", nullable: false),
                    EmbeddingTokens = table.Column<long>(type: "bigint", nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,10)", precision: 18, scale: 10, nullable: false),
                    OperationCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenUsageExecutionTotals", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_NotInTotals",
                schema: "UserData",
                table: "TokenUsages",
                columns: new[] { "OrganizationId", "CreatedAt" },
                filter: "\"IsInTotals\" = false")
                .Annotation("Npgsql:IndexInclude", new[] { "IsDeleted", "UsageType", "WorkflowTypeId", "UserId", "WorkflowExecutionId" });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsageDailyTotals_OrganizationId_Day",
                schema: "UserData",
                table: "TokenUsageDailyTotals",
                columns: new[] { "OrganizationId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsageExecutionTotals_OrganizationId_WorkflowTypeId_Fir~",
                schema: "UserData",
                table: "TokenUsageExecutionTotals",
                columns: new[] { "OrganizationId", "WorkflowTypeId", "FirstUsageAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsageExecutionTotals_WorkflowExecutionId",
                schema: "UserData",
                table: "TokenUsageExecutionTotals",
                column: "WorkflowExecutionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TokenUsageDailyTotals",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "TokenUsageExecutionTotals",
                schema: "UserData");

            migrationBuilder.DropIndex(
                name: "IX_TokenUsages_NotInTotals",
                schema: "UserData",
                table: "TokenUsages");

            migrationBuilder.DropColumn(
                name: "IsInTotals",
                schema: "UserData",
                table: "TokenUsages");
        }
    }
}

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
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
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TokenUsageDailyTotals",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    UsageType = table.Column<int>(type: "int", nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheWriteTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheReadTokens = table.Column<long>(type: "bigint", nullable: false),
                    EmbeddingTokens = table.Column<long>(type: "bigint", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: false),
                    OperationCount = table.Column<int>(type: "int", nullable: false)
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
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    FirstUsageAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheWriteTokens = table.Column<long>(type: "bigint", nullable: false),
                    CacheReadTokens = table.Column<long>(type: "bigint", nullable: false),
                    EmbeddingTokens = table.Column<long>(type: "bigint", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: false),
                    OperationCount = table.Column<int>(type: "int", nullable: false)
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
                filter: "[IsInTotals] = 0")
                .Annotation("SqlServer:Include", new[] { "IsDeleted", "UsageType", "WorkflowTypeId", "UserId", "WorkflowExecutionId" });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsageDailyTotals_OrganizationId_Day",
                schema: "UserData",
                table: "TokenUsageDailyTotals",
                columns: new[] { "OrganizationId", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsageExecutionTotals_OrganizationId_WorkflowTypeId_FirstUsageAt",
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

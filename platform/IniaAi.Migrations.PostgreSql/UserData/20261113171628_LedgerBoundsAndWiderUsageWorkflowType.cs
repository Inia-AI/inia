using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class LedgerBoundsAndWiderUsageWorkflowType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "WorkflowTypeId",
                schema: "UserData",
                table: "TokenUsages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(36)",
                oldMaxLength: 36,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowTypeId",
                schema: "UserData",
                table: "TokenUsageExecutionTotals",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(36)",
                oldMaxLength: 36,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowTypeId",
                schema: "UserData",
                table: "TokenUsageDailyTotals",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(36)",
                oldMaxLength: 36,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditPacks_DebtRepaidWithinAmount",
                schema: "UserData",
                table: "CreditPacks",
                sql: "\"DebtRepaid\" >= 0 AND \"DebtRepaid\" <= \"Amount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditPacks_RemainingWithinAmount",
                schema: "UserData",
                table: "CreditPacks",
                sql: "\"Remaining\" >= 0 AND \"Remaining\" <= \"Amount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditPacks_ReversedWithinAmount",
                schema: "UserData",
                table: "CreditPacks",
                sql: "\"Reversed\" >= 0 AND \"Reversed\" <= \"Amount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditAccounts_DebtNotNegative",
                schema: "UserData",
                table: "CreditAccounts",
                sql: "\"Debt\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreditAccounts_PendingNotNegative",
                schema: "UserData",
                table: "CreditAccounts",
                sql: "\"Pending\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditPacks_DebtRepaidWithinAmount",
                schema: "UserData",
                table: "CreditPacks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditPacks_RemainingWithinAmount",
                schema: "UserData",
                table: "CreditPacks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditPacks_ReversedWithinAmount",
                schema: "UserData",
                table: "CreditPacks");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditAccounts_DebtNotNegative",
                schema: "UserData",
                table: "CreditAccounts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreditAccounts_PendingNotNegative",
                schema: "UserData",
                table: "CreditAccounts");

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowTypeId",
                schema: "UserData",
                table: "TokenUsages",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowTypeId",
                schema: "UserData",
                table: "TokenUsageExecutionTotals",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "WorkflowTypeId",
                schema: "UserData",
                table: "TokenUsageDailyTotals",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class AddCreditAndRunStateIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_OrganizationId_WorkflowState_CompletedAt",
                schema: "UserData",
                table: "WorkflowExecutions",
                columns: new[] { "OrganizationId", "WorkflowState", "CompletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CreditEntries_Status_OrganizationId",
                schema: "UserData",
                table: "CreditEntries",
                columns: new[] { "Status", "OrganizationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkflowExecutions_OrganizationId_WorkflowState_CompletedAt",
                schema: "UserData",
                table: "WorkflowExecutions");

            migrationBuilder.DropIndex(
                name: "IX_CreditEntries_Status_OrganizationId",
                schema: "UserData",
                table: "CreditEntries");
        }
    }
}

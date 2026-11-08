using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class DropWorkflowReportState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WorkflowState",
                schema: "UserData",
                table: "WorkflowReports");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkflowState",
                schema: "UserData",
                table: "WorkflowReports",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}

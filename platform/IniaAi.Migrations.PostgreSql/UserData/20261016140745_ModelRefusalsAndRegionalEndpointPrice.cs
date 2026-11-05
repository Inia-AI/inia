using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class ModelRefusalsAndRegionalEndpointPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RefusalCategory",
                schema: "UserData",
                table: "WorkflowExecutionLogs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefusalCategory",
                schema: "UserData",
                table: "Pages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefusalCategory",
                schema: "UserData",
                table: "WorkflowExecutionLogs");

            migrationBuilder.DropColumn(
                name: "RefusalCategory",
                schema: "UserData",
                table: "Pages");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.Core
{
    /// <inheritdoc />
    public partial class DocumentClassMadeBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MadeByDocumentClassKey",
                schema: "Core",
                table: "DocumentClasses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MadeByWorkflowTypeId",
                schema: "Core",
                table: "DocumentClasses",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MadeByDocumentClassKey",
                schema: "Core",
                table: "DocumentClasses");

            migrationBuilder.DropColumn(
                name: "MadeByWorkflowTypeId",
                schema: "Core",
                table: "DocumentClasses");
        }
    }
}

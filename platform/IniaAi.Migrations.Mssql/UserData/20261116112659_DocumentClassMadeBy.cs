using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
{
    /// <inheritdoc />
    public partial class DocumentClassMadeBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MadeByDocumentClassKey",
                schema: "UserData",
                table: "DocumentClasses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MadeByWorkflowTypeId",
                schema: "UserData",
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
                schema: "UserData",
                table: "DocumentClasses");

            migrationBuilder.DropColumn(
                name: "MadeByWorkflowTypeId",
                schema: "UserData",
                table: "DocumentClasses");
        }
    }
}

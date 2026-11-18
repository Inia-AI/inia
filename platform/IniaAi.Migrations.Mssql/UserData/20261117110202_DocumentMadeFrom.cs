using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
{
    /// <inheritdoc />
    public partial class DocumentMadeFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MadeFromDocumentId",
                schema: "UserData",
                table: "Documents",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_MadeFromDocumentId",
                schema: "UserData",
                table: "Documents",
                column: "MadeFromDocumentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Documents_MadeFromDocumentId",
                schema: "UserData",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "MadeFromDocumentId",
                schema: "UserData",
                table: "Documents");
        }
    }
}

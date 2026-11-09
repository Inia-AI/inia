using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
{
    /// <inheritdoc />
    public partial class DocumentEmailPages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CountedAttachmentPages",
                schema: "UserData",
                table: "Documents",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailIdentifier",
                schema: "UserData",
                table: "Documents",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CountedAttachmentPages",
                schema: "UserData",
                table: "Documents");

            migrationBuilder.DropColumn(
                name: "EmailIdentifier",
                schema: "UserData",
                table: "Documents");
        }
    }
}

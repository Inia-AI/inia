using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
{
    /// <inheritdoc />
    public partial class FreeCreditPersonKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PersonKey",
                schema: "UserData",
                table: "CreditPacks",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditPacks_PersonKey",
                schema: "UserData",
                table: "CreditPacks",
                column: "PersonKey",
                unique: true,
                filter: "[PersonKey] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CreditPacks_PersonKey",
                schema: "UserData",
                table: "CreditPacks");

            migrationBuilder.DropColumn(
                name: "PersonKey",
                schema: "UserData",
                table: "CreditPacks");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
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
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditPacks_PersonKey",
                schema: "UserData",
                table: "CreditPacks",
                column: "PersonKey",
                unique: true);
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

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class SelfServePrepaidBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BillingCountry",
                schema: "UserData",
                table: "CreditPacks",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BillingState",
                schema: "UserData",
                table: "CreditPacks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardFingerprint",
                schema: "UserData",
                table: "CreditPacks",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WelcomeBonus",
                schema: "UserData",
                table: "CreditPacks",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BillingCountry",
                schema: "UserData",
                table: "CreditPacks");

            migrationBuilder.DropColumn(
                name: "BillingState",
                schema: "UserData",
                table: "CreditPacks");

            migrationBuilder.DropColumn(
                name: "CardFingerprint",
                schema: "UserData",
                table: "CreditPacks");

            migrationBuilder.DropColumn(
                name: "WelcomeBonus",
                schema: "UserData",
                table: "CreditPacks");
        }
    }
}

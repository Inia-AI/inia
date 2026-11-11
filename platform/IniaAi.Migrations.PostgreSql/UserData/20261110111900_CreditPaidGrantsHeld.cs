using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class CreditPaidGrantsHeld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PaidGrantsHeldAt",
                schema: "UserData",
                table: "CreditAccounts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaidGrantsHeldAt",
                schema: "UserData",
                table: "CreditAccounts");
        }
    }
}

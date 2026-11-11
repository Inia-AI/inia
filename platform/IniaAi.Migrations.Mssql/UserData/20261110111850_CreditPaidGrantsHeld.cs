using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
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
                type: "datetime2",
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

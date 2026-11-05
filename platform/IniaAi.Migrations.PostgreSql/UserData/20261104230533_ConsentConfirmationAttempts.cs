using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class ConsentConfirmationAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConfirmationAttempts",
                schema: "UserData",
                table: "Consents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ConfirmationLastAttemptAt",
                schema: "UserData",
                table: "Consents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmationLastError",
                schema: "UserData",
                table: "Consents",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Consents_ConfirmationSentAt_ConfirmationAttempts_GivenAt",
                schema: "UserData",
                table: "Consents",
                columns: new[] { "ConfirmationSentAt", "ConfirmationAttempts", "GivenAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Consents_ConfirmationSentAt_ConfirmationAttempts_GivenAt",
                schema: "UserData",
                table: "Consents");

            migrationBuilder.DropColumn(
                name: "ConfirmationAttempts",
                schema: "UserData",
                table: "Consents");

            migrationBuilder.DropColumn(
                name: "ConfirmationLastAttemptAt",
                schema: "UserData",
                table: "Consents");

            migrationBuilder.DropColumn(
                name: "ConfirmationLastError",
                schema: "UserData",
                table: "Consents");
        }
    }
}

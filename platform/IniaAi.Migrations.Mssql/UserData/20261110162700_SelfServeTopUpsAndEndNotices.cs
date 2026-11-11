using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
{
    /// <inheritdoc />
    public partial class SelfServeTopUpsAndEndNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                schema: "UserData",
                table: "Subscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EndNoticeAttempts",
                schema: "UserData",
                table: "Subscriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndNoticeLastAttemptAt",
                schema: "UserData",
                table: "Subscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndNoticeLastError",
                schema: "UserData",
                table: "Subscriptions",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndNoticeSentAt",
                schema: "UserData",
                table: "Subscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndsAt",
                schema: "UserData",
                table: "Subscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderInvoiceId",
                schema: "UserData",
                table: "Consents",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TopUpPriceId",
                schema: "UserData",
                table: "Consents",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_EndNoticeSentAt_CanceledAt",
                schema: "UserData",
                table: "Subscriptions",
                columns: new[] { "EndNoticeSentAt", "CanceledAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_EndNoticeSentAt_CanceledAt",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "EndNoticeAttempts",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "EndNoticeLastAttemptAt",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "EndNoticeLastError",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "EndNoticeSentAt",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "EndsAt",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ProviderInvoiceId",
                schema: "UserData",
                table: "Consents");

            migrationBuilder.DropColumn(
                name: "TopUpPriceId",
                schema: "UserData",
                table: "Consents");
        }
    }
}

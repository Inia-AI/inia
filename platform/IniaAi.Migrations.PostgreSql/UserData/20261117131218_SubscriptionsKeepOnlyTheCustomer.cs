using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class SubscriptionsKeepOnlyTheCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_EndNoticeSentAt_CanceledAt",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_PaymentProvider_LiveOrganizationId",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_PaymentProvider_ProviderSubscriptionId",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CancelAtPeriodEnd",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CanceledAt",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "CurrentPeriodEnd",
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
                name: "LiveOrganizationId",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "ProviderSubscriptionId",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                schema: "UserData",
                table: "Subscriptions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "CancelAtPeriodEnd",
                schema: "UserData",
                table: "Subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "CanceledAt",
                schema: "UserData",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentPeriodEnd",
                schema: "UserData",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EndNoticeAttempts",
                schema: "UserData",
                table: "Subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndNoticeLastAttemptAt",
                schema: "UserData",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndNoticeLastError",
                schema: "UserData",
                table: "Subscriptions",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndNoticeSentAt",
                schema: "UserData",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EndsAt",
                schema: "UserData",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LiveOrganizationId",
                schema: "UserData",
                table: "Subscriptions",
                type: "character varying(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProviderSubscriptionId",
                schema: "UserData",
                table: "Subscriptions",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "UserData",
                table: "Subscriptions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                schema: "UserData",
                table: "Subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_EndNoticeSentAt_CanceledAt",
                schema: "UserData",
                table: "Subscriptions",
                columns: new[] { "EndNoticeSentAt", "CanceledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PaymentProvider_LiveOrganizationId",
                schema: "UserData",
                table: "Subscriptions",
                columns: new[] { "PaymentProvider", "LiveOrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_PaymentProvider_ProviderSubscriptionId",
                schema: "UserData",
                table: "Subscriptions",
                columns: new[] { "PaymentProvider", "ProviderSubscriptionId" },
                unique: true);
        }
    }
}

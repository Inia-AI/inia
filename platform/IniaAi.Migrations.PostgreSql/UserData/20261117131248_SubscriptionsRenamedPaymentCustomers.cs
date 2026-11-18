using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class SubscriptionsRenamedPaymentCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Subscriptions",
                schema: "UserData",
                table: "Subscriptions");

            migrationBuilder.RenameTable(
                name: "Subscriptions",
                schema: "UserData",
                newName: "PaymentCustomers",
                newSchema: "UserData");

            migrationBuilder.RenameIndex(
                name: "IX_Subscriptions_OrganizationId",
                schema: "UserData",
                table: "PaymentCustomers",
                newName: "IX_PaymentCustomers_OrganizationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_PaymentCustomers",
                schema: "UserData",
                table: "PaymentCustomers",
                column: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_PaymentCustomers",
                schema: "UserData",
                table: "PaymentCustomers");

            migrationBuilder.RenameTable(
                name: "PaymentCustomers",
                schema: "UserData",
                newName: "Subscriptions",
                newSchema: "UserData");

            migrationBuilder.RenameIndex(
                name: "IX_PaymentCustomers_OrganizationId",
                schema: "UserData",
                table: "Subscriptions",
                newName: "IX_Subscriptions_OrganizationId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Subscriptions",
                schema: "UserData",
                table: "Subscriptions",
                column: "Id");
        }
    }
}

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
{
    /// <inheritdoc />
    public partial class WelcomeBonusPaymentMethodFingerprint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CardFingerprint",
                schema: "UserData",
                table: "CreditPacks",
                newName: "PaymentMethodFingerprint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "PaymentMethodFingerprint",
                schema: "UserData",
                table: "CreditPacks",
                newName: "CardFingerprint");
        }
    }
}

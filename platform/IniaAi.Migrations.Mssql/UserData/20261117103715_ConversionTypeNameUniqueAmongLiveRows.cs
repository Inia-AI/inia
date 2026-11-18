using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
{
    /// <inheritdoc />
    public partial class ConversionTypeNameUniqueAmongLiveRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConversionTypes_OrganizationId_Name_IsDeleted",
                schema: "UserData",
                table: "ConversionTypes");

            migrationBuilder.CreateIndex(
                name: "IX_ConversionTypes_OrganizationId_Name",
                schema: "UserData",
                table: "ConversionTypes",
                columns: new[] { "OrganizationId", "Name" },
                unique: true,
                filter: "[IsDeleted] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ConversionTypes_OrganizationId_Name",
                schema: "UserData",
                table: "ConversionTypes");

            migrationBuilder.CreateIndex(
                name: "IX_ConversionTypes_OrganizationId_Name_IsDeleted",
                schema: "UserData",
                table: "ConversionTypes",
                columns: new[] { "OrganizationId", "Name", "IsDeleted" },
                unique: true);
        }
    }
}

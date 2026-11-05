using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.Core
{
    /// <inheritdoc />
    public partial class ModelRefusalsAndRegionalEndpointPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RegionalEndpointMultiplier",
                schema: "Core",
                table: "ModelPricings",
                type: "decimal(6,4)",
                precision: 6,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RegionalEndpointMultiplier",
                schema: "Core",
                table: "ModelPricings");
        }
    }
}

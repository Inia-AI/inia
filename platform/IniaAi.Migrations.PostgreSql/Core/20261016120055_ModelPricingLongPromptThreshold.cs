using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.Core
{
    /// <inheritdoc />
    public partial class ModelPricingLongPromptThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LongPromptThresholdTokens",
                schema: "Core",
                table: "ModelPricings",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LongPromptThresholdTokens",
                schema: "Core",
                table: "ModelPricings");
        }
    }
}

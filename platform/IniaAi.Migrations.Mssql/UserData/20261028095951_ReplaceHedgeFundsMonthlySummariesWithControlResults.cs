using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
{
    /// <inheritdoc />
    public partial class ReplaceHedgeFundsMonthlySummariesWithControlResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HedgeFundsMonthlySummaries",
                schema: "UserData");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                schema: "UserData",
                table: "HedgeFundsMonitoringObservations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                schema: "UserData",
                table: "HedgeFundsMonitoringObservations",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HedgeFundsControlResults",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HedgeFundsMonitoringObservationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Control = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    CurrentValue = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: true),
                    ComparedValue = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: true),
                    MeasuredValue = table.Column<decimal>(type: "decimal(28,6)", precision: 28, scale: 6, nullable: true),
                    Threshold = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: true),
                    CurrentCurrency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ComparedCurrency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ComparedObservationId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ComparedReportingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HedgeFundsControlResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HedgeFundsControlResults_HedgeFundsMonitoringObservations_HedgeFundsMonitoringObservationId",
                        column: x => x.HedgeFundsMonitoringObservationId,
                        principalSchema: "UserData",
                        principalTable: "HedgeFundsMonitoringObservations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HedgeFundsControlResults_HedgeFundsMonitoringObservationId",
                schema: "UserData",
                table: "HedgeFundsControlResults",
                column: "HedgeFundsMonitoringObservationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HedgeFundsControlResults",
                schema: "UserData");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                schema: "UserData",
                table: "HedgeFundsMonitoringObservations");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                schema: "UserData",
                table: "HedgeFundsMonitoringObservations");

            migrationBuilder.CreateTable(
                name: "HedgeFundsMonthlySummaries",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FundsWithMissingObservations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FundsWithMultipleObservations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FundsWithObservationsWithErrors = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FundsWithObservationsWithWarnings = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    ObservationsWithErrorsCount = table.Column<int>(type: "int", nullable: false),
                    ObservationsWithWarningsCount = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SummaryMonth = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalFundsExpected = table.Column<int>(type: "int", nullable: false),
                    TotalObservationsCreated = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HedgeFundsMonthlySummaries", x => x.Id);
                });
        }
    }
}

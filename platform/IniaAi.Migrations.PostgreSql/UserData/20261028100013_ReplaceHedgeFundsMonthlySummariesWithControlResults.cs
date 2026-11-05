using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
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
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                schema: "UserData",
                table: "HedgeFundsMonitoringObservations",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "HedgeFundsControlResults",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    HedgeFundsMonitoringObservationId = table.Column<string>(type: "text", nullable: false),
                    Control = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    CurrentValue = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: true),
                    ComparedValue = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: true),
                    MeasuredValue = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: true),
                    Threshold = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    CurrentCurrency = table.Column<string>(type: "text", nullable: true),
                    ComparedCurrency = table.Column<string>(type: "text", nullable: true),
                    ComparedObservationId = table.Column<string>(type: "text", nullable: true),
                    ComparedReportingDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: true),
                    CheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HedgeFundsControlResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HedgeFundsControlResults_HedgeFundsMonitoringObservations_H~",
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
                    Id = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "text", nullable: true),
                    FundsWithMissingObservations = table.Column<List<string>>(type: "text[]", nullable: false),
                    FundsWithMultipleObservations = table.Column<List<string>>(type: "text[]", nullable: false),
                    FundsWithObservationsWithErrors = table.Column<List<string>>(type: "text[]", nullable: false),
                    FundsWithObservationsWithWarnings = table.Column<List<string>>(type: "text[]", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    ObservationsWithErrorsCount = table.Column<int>(type: "integer", nullable: false),
                    ObservationsWithWarningsCount = table.Column<int>(type: "integer", nullable: false),
                    OrganizationId = table.Column<string>(type: "text", nullable: false),
                    SummaryMonth = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalFundsExpected = table.Column<int>(type: "integer", nullable: false),
                    TotalObservationsCreated = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HedgeFundsMonthlySummaries", x => x.Id);
                });
        }
    }
}

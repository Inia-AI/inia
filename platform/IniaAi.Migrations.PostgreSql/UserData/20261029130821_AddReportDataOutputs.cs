using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.PostgreSql.UserData
{
    /// <inheritdoc />
    public partial class AddReportDataOutputs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DataOutputName",
                schema: "UserData",
                table: "Blocks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReportDataOutputs",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    WorkflowReportId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: false),
                    BlockId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false),
                    Citations = table.Column<string>(type: "jsonb", nullable: false),
                    CitedSourcesByBlock = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportDataOutputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportDataOutputs_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "UserData",
                        principalTable: "Blocks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReportDataOutputs_WorkflowReports_WorkflowReportId",
                        column: x => x.WorkflowReportId,
                        principalSchema: "UserData",
                        principalTable: "WorkflowReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportDataOutputs_BlockId",
                schema: "UserData",
                table: "ReportDataOutputs",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportDataOutputs_WorkflowReportId_Name",
                schema: "UserData",
                table: "ReportDataOutputs",
                columns: new[] { "WorkflowReportId", "Name" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportDataOutputs",
                schema: "UserData");

            migrationBuilder.DropColumn(
                name: "DataOutputName",
                schema: "UserData",
                table: "Blocks");
        }
    }
}

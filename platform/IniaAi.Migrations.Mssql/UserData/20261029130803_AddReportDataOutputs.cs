using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
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
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ReportDataOutputs",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowReportId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Citations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CitedSourcesByBlock = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
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

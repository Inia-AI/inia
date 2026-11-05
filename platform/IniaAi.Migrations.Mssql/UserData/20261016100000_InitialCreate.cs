using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.UserData
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "UserData");

            migrationBuilder.CreateTable(
                name: "AgentConversations",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ModelId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Effort = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastActivityAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentConversations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Applications",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ApplicationType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientSecret = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClientType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConsentType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayNames = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JsonWebKeySet = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Permissions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PostLogoutRedirectUris = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RedirectUris = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Requirements = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Settings = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Applications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConversionTypes",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversionTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CounterpartyTypes",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterpartyTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocumentUnlockAuditLogs",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DocumentName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorUserEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    PasswordSupplied = table.Column<bool>(type: "bit", nullable: false),
                    ContainedSignature = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentUnlockAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EditorAgentSpendUnlocks",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    FromBatches = table.Column<int>(type: "int", nullable: false),
                    Batches = table.Column<int>(type: "int", nullable: false),
                    UnlockedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorAgentSpendUnlocks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmbeddingVectors",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Model = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Vector = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmbeddingVectors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Files",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Bytes = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DocumentId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "graph_edges",
                schema: "UserData",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SourceId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_graph_edges", x => new { x.RunId, x.SourceId, x.TargetId });
                });

            migrationBuilder.CreateTable(
                name: "graph_nodes",
                schema: "UserData",
                columns: table => new
                {
                    RunId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Order = table.Column<int>(type: "int", nullable: true),
                    DocumentClass = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ReplacementTag = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_graph_nodes", x => new { x.RunId, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "HedgeFundsMonitoringObservations",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FundId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FundManagerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FundManager = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FundName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PerformancePercent = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    NetAssetValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NavCurrency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JobId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Units = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WarningMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClearedWarningMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TriggerEmailIdentifier = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsBaseline = table.Column<bool>(type: "bit", nullable: false),
                    IsExternal = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HedgeFundsMonitoringObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HedgeFundsMonthlySummaries",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SummaryMonth = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FundsWithMissingObservations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FundsWithObservationsWithWarnings = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FundsWithObservationsWithErrors = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FundsWithMultipleObservations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TotalFundsExpected = table.Column<int>(type: "int", nullable: false),
                    TotalObservationsCreated = table.Column<int>(type: "int", nullable: false),
                    ObservationsWithWarningsCount = table.Column<int>(type: "int", nullable: false),
                    ObservationsWithErrorsCount = table.Column<int>(type: "int", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HedgeFundsMonthlySummaries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Organizations",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    ParallelApiKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PerplexityApiKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FmpApiKey = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SignatureReviewThreshold = table.Column<int>(type: "int", nullable: false),
                    EditorAgentDailySpendBatchUsd = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PrivateEquityMonitoringObservations",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FundId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    FundManagerId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FundManager = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FundName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JobId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReportingDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NetAssetValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NetWorth = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    GrossIrr = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    NetIrr = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: true),
                    RecallableCapital = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    UncalledCapital = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalCommitment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Distributions = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalAssets = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    TotalLiabilities = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    NumberOfInvestments = table.Column<int>(type: "int", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FundStrategy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FundAssetGeography = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FundAssetSector = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Vintage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Term = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcentrationSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcentrationFlags = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DiversificationAssessment = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WarningMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClearedWarningMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsApproved = table.Column<bool>(type: "bit", nullable: false),
                    IsBaseline = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateEquityMonitoringObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefinementPresets",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Instruction = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    IsShared = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefinementPresets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RegressionTestResults",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Version = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Report = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Results = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RegressionTestResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReportFeedbackItems",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    QuotedText = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    Category = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AuthorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DismissReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StatusChangedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AddressedInVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportFeedbackItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scopes",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ConcurrencyToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Descriptions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayNames = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Resources = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scopes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningBacktestDocuments",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BacktestId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    RunVariablesSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserClassesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsJudged = table.Column<bool>(type: "bit", nullable: false),
                    DuplicateOfId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ContentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    Readable = table.Column<bool>(type: "bit", nullable: true),
                    MatchedClassesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BestMatch = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Bucket = table.Column<int>(type: "int", nullable: true),
                    ReadFailure = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Reasoning = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    EvidenceExcerpt = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ContentClearedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PromptInjectionSuspected = table.Column<bool>(type: "bit", nullable: false),
                    MaliciousContentSuspected = table.Column<bool>(type: "bit", nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodMonths = table.Column<int>(type: "int", nullable: true),
                    JudgedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningBacktestDocuments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningBacktestRuns",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BacktestId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RunCreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RunVariablesSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VariablesWithheld = table.Column<bool>(type: "bit", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    RefusalReason = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    DocumentsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningBacktestRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningBacktests",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    StartedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StartedByOrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    DocumentCap = table.Column<int>(type: "int", nullable: false),
                    PlannedDocumentCount = table.Column<int>(type: "int", nullable: false),
                    PlannedRunCount = table.Column<int>(type: "int", nullable: false),
                    JudgedDocumentCount = table.Column<int>(type: "int", nullable: false),
                    ScoredCount = table.Column<int>(type: "int", nullable: false),
                    AgreedCount = table.Column<int>(type: "int", nullable: false),
                    DecidedRunCount = table.Column<int>(type: "int", nullable: false),
                    TemplateVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TemplateContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PromptsRowId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    JudgeSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JudgePrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClassesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VariableLabelsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Guidance = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelRequestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningBacktests", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningRecords",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DocumentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    OriginatingDocumentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    FileName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    ContentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DetectedType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MatchedClassesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssignedClass = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Disposition = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReasonCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PromptInjectionSuspected = table.Column<bool>(type: "bit", nullable: false),
                    MaliciousContentSuspected = table.Column<bool>(type: "bit", nullable: false),
                    EvidenceExcerpt = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    Reasoning = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExcerptClearedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModelId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PeriodEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    PeriodMonths = table.Column<int>(type: "int", nullable: true),
                    PromptsRowId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValidationCases",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    NameIsDerived = table.Column<bool>(type: "bit", nullable: false),
                    SchemaId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BaselineDocumentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    BaselineExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    BaselineTemplateVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    BaselineTemplateContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BaselineTextCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    BaselineTextLength = table.Column<int>(type: "int", nullable: false),
                    BaselineTextHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BaselineNotesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResolvedSchemaJsonCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    SelectedFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RerunRecipeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReferenceJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtractionFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReferenceModelId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReferenceExtractedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReferenceExtractionStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReferenceExtractionError = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReferenceRevision = table.Column<int>(type: "int", nullable: false),
                    SelectedFieldCount = table.Column<int>(type: "int", nullable: false),
                    ConfirmedFieldCount = table.Column<int>(type: "int", nullable: false),
                    ReferenceInputTokens = table.Column<int>(type: "int", nullable: false),
                    ReferenceOutputTokens = table.Column<int>(type: "int", nullable: false),
                    ReferenceCost = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    SourceInstance = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceCaseId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    FreezeCapturesJsonCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationCases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValidationReferenceAuditLogs",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    RowKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    FieldName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreviousValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorUserEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationReferenceAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValidationSchedules",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TargetKind = table.Column<int>(type: "int", nullable: false),
                    TargetId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Cadence = table.Column<int>(type: "int", nullable: false),
                    Weekday = table.Column<int>(type: "int", nullable: true),
                    EnrolledByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    LastRunAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastBatchId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    LastFailureAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastFailureMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationSchedules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValidationSuites",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    CaseIdsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MembershipRevision = table.Column<int>(type: "int", nullable: false),
                    SourceInstance = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceSuiteId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationSuites", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValidationThresholdSettings",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SuccessAtOrAbovePercent = table.Column<double>(type: "float", nullable: false),
                    WarningAtOrAbovePercent = table.Column<double>(type: "float", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationThresholdSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VerificationCriterionRecords",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TemplateVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TemplateContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    VerifierBlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    MemberBlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    MemberTag = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Round = table.Column<int>(type: "int", nullable: false),
                    RunExecutionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    CriterionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CriterionLabel = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CriterionDefinitionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Met = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AdvisoryOutcome = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    AdvisoryMemberVerdict = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ConfiguredRedoMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ActualRedoMode = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    EvidenceSpanCount = table.Column<int>(type: "int", nullable: false),
                    EvidenceSpans = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Feedback = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EstimatedCostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    JudgedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerificationCriterionRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowBaselineRegistrations",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CounterpartyId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CounterpartyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MarkedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MarkedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BlessedContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    BlessedVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    SupersededByRegistrationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SourceInstance = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceBlessedContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowBaselineRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRegressionRuns",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BaselineExecutionId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RerunExecutionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkflowTypeName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CounterpartyName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OverallSimilarityPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    BlocksCompared = table.Column<int>(type: "int", nullable: false),
                    BlocksMatched = table.Column<int>(type: "int", nullable: false),
                    BlocksDiffered = table.Column<int>(type: "int", nullable: false),
                    BlocksWithDefinitionChanges = table.Column<int>(type: "int", nullable: false),
                    UnexpectedDifferences = table.Column<int>(type: "int", nullable: false),
                    ExpectedDifferences = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TimeoutMinutes = table.Column<int>(type: "int", nullable: true),
                    TimeoutAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TriggeredByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ErrorMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    PlatformVersion = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    InconclusiveReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRegressionRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AgentConversationTurns",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    UserMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AnswerMarkdown = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    RunExecutionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    RunCount = table.Column<int>(type: "int", nullable: false),
                    ResumedSession = table.Column<bool>(type: "bit", nullable: false),
                    CostUsd = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    Error = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ActivityJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JobId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    HostInstanceId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    ContextRef = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DispatchedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentConversationTurns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentConversationTurns_AgentConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "UserData",
                        principalTable: "AgentConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EditorAgentDrafts",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    ContentSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    WorkflowJsonCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorAgentDrafts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditorAgentDrafts_AgentConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "UserData",
                        principalTable: "AgentConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EditorAgentTurnFeedback",
                schema: "UserData",
                columns: table => new
                {
                    TurnId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    FeedbackItemIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OpenCount = table.Column<int>(type: "int", nullable: false),
                    DismissSuggestionsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DismissFileSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorAgentTurnFeedback", x => x.TurnId);
                    table.ForeignKey(
                        name: "FK_EditorAgentTurnFeedback_AgentConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "UserData",
                        principalTable: "AgentConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EditorAgentTurnReports",
                schema: "UserData",
                columns: table => new
                {
                    TurnId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PickedExecutionIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    FetchedExecutionIds = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    OpenRequestSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorAgentTurnReports", x => x.TurnId);
                    table.ForeignKey(
                        name: "FK_EditorAgentTurnReports_AgentConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "UserData",
                        principalTable: "AgentConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Authorizations",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ApplicationId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Scopes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Authorizations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Authorizations_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "UserData",
                        principalTable: "Applications",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "UserData",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HedgeFundsMonitoringObservationAudits",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    HedgeFundsMonitoringObservationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HedgeFundsMonitoringObservationAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HedgeFundsMonitoringObservationAudits_HedgeFundsMonitoringObservations_HedgeFundsMonitoringObservationId",
                        column: x => x.HedgeFundsMonitoringObservationId,
                        principalSchema: "UserData",
                        principalTable: "HedgeFundsMonitoringObservations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MiddleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    UserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SecurityStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "bit", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "bit", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "bit", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "UserData",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Counterparties",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ParentId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    NameVariants = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Website = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    LastFinancialAnalysisResult = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Counterparties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Counterparties_Counterparties_ParentId",
                        column: x => x.ParentId,
                        principalSchema: "UserData",
                        principalTable: "Counterparties",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Counterparties_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "UserData",
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FileMetadata",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Extension = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageProvider = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    ExternalStoragePath = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileMetadata", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileMetadata_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "UserData",
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OrganizationCleanupSettings",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RetentionDays = table.Column<int>(type: "int", nullable: false),
                    CleanupDayOfWeek = table.Column<int>(type: "int", nullable: false),
                    CleanupHourUtc = table.Column<int>(type: "int", nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false),
                    HasExplicitSettings = table.Column<bool>(type: "bit", nullable: false),
                    LastCleanupAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationCleanupSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationCleanupSettings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "UserData",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationModelSettings",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    UsageContext = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ModelPricingId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    Priority = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationModelSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationModelSettings_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "UserData",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrivateEquityMonitoringAllocations",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PrivateEquityMonitoringObservationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sector = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Geography = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FairMarketValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    Weightage = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateEquityMonitoringAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateEquityMonitoringAllocations_PrivateEquityMonitoringObservations_PrivateEquityMonitoringObservationId",
                        column: x => x.PrivateEquityMonitoringObservationId,
                        principalSchema: "UserData",
                        principalTable: "PrivateEquityMonitoringObservations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrivateEquityMonitoringObservationAudits",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PrivateEquityMonitoringObservationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateEquityMonitoringObservationAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PrivateEquityMonitoringObservationAudits_PrivateEquityMonitoringObservations_PrivateEquityMonitoringObservationId",
                        column: x => x.PrivateEquityMonitoringObservationId,
                        principalSchema: "UserData",
                        principalTable: "PrivateEquityMonitoringObservations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ValidationRuns",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CaseId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<int>(type: "int", nullable: true),
                    ResolvedSchemaJsonCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    ReferenceSnapshotJsonCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CandidateExtractionJsonCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CandidateTextCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CandidateTextLength = table.Column<int>(type: "int", nullable: false),
                    CandidateTextHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    VerdictsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScorecardJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NotesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InputDocumentsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InputsChangedSinceBaseline = table.Column<bool>(type: "bit", nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    AdjudicationJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MatchRatePercent = table.Column<double>(type: "float", nullable: true),
                    WeightedRatePercent = table.Column<double>(type: "float", nullable: true),
                    ScoreableFieldCount = table.Column<int>(type: "int", nullable: false),
                    MatchedFieldCount = table.Column<int>(type: "int", nullable: false),
                    UnsettledFieldCount = table.Column<int>(type: "int", nullable: false),
                    AdjudicatedMatchRatePercent = table.Column<double>(type: "float", nullable: true),
                    AdjudicatedWeightedRatePercent = table.Column<double>(type: "float", nullable: true),
                    AdjudicatedOutcome = table.Column<int>(type: "int", nullable: true),
                    AdjudicatedScoreableFieldCount = table.Column<int>(type: "int", nullable: true),
                    AdjudicatedMatchedFieldCount = table.Column<int>(type: "int", nullable: true),
                    JudgeOverrideCount = table.Column<int>(type: "int", nullable: false),
                    FreezeReplayedCount = table.Column<int>(type: "int", nullable: true),
                    FreezeEligibleCount = table.Column<int>(type: "int", nullable: true),
                    TotalInputTokens = table.Column<long>(type: "bigint", nullable: false),
                    TotalOutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    TotalCost = table.Column<decimal>(type: "decimal(18,10)", precision: 18, scale: 10, nullable: false),
                    CandidateSourceKind = table.Column<int>(type: "int", nullable: false),
                    CandidateSourceId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CandidateWorkflowTypeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CandidateExecutedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BaselineTemplateVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    BaselineTemplateContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CandidateTemplateVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CandidateTemplateContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExtractorModelId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    JudgeModelId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    PromptSetHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExtractionFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReferenceRevisionUsed = table.Column<int>(type: "int", nullable: false),
                    BatchId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RepeatIndex = table.Column<int>(type: "int", nullable: false),
                    SuiteId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActTargetKind = table.Column<int>(type: "int", nullable: false),
                    ActTargetId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TriggeredByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Trigger = table.Column<int>(type: "int", nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeadlineAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Accepted = table.Column<bool>(type: "bit", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AcceptedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PayloadTrimmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ValidationRuns_ValidationCases_CaseId",
                        column: x => x.CaseId,
                        principalSchema: "UserData",
                        principalTable: "ValidationCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRegressionBlockResults",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WorkflowRegressionRunId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Category = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Fact = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Sources = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BaselineValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RerunValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    SimilarityScore = table.Column<decimal>(type: "decimal(5,2)", nullable: false),
                    BaselineVariations = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RerunVariations = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BlockName = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    BlockType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DefinitionChanged = table.Column<bool>(type: "bit", nullable: false),
                    DefinitionDiffSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BaselineNumeric = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    RerunNumeric = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    Tolerance = table.Column<decimal>(type: "decimal(28,8)", nullable: true),
                    SchemaFieldType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRegressionBlockResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowRegressionBlockResults_WorkflowRegressionRuns_WorkflowRegressionRunId",
                        column: x => x.WorkflowRegressionRunId,
                        principalSchema: "UserData",
                        principalTable: "WorkflowRegressionRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EditorAgentProposals",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TurnId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ConversationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DraftId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProposedWorkflowSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ProposalJsonCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UndoJsonCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorAgentProposals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditorAgentProposals_AgentConversationTurns_TurnId",
                        column: x => x.TurnId,
                        principalSchema: "UserData",
                        principalTable: "AgentConversationTurns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Tokens",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ApplicationId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    AuthorizationId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ConcurrencyToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpirationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RedemptionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReferenceId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tokens_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalSchema: "UserData",
                        principalTable: "Applications",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Tokens_Authorizations_AuthorizationId",
                        column: x => x.AuthorizationId,
                        principalSchema: "UserData",
                        principalTable: "Authorizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClaimType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ClaimValue = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "UserData",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                schema: "UserData",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderKey = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "UserData",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                schema: "UserData",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RoleId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalSchema: "UserData",
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "UserData",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                schema: "UserData",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LoginProvider = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "UserData",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserOrganizations",
                schema: "UserData",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    JoinedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    LeftAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserOrganizations", x => new { x.UserId, x.OrganizationId });
                    table.ForeignKey(
                        name: "FK_UserOrganizations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "UserData",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserOrganizations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "UserData",
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserTrustedDevices",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DeviceFingerprint = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DeviceName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    TrustedUntil = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTrustedDevices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserTrustedDevices_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "UserData",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserWorkflowTypeVisibilities",
                schema: "UserData",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserWorkflowTypeVisibilities", x => new { x.UserId, x.WorkflowTypeId });
                    table.ForeignKey(
                        name: "FK_UserWorkflowTypeVisibilities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "UserData",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Workflows",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Configuration = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TextInputTemplate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParameterizedConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpandedFromParameterizedKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionState = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workflows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Workflows_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "UserData",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Workflows_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "UserData",
                        principalTable: "Organizations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CounterpartyCounterpartyType",
                schema: "UserData",
                columns: table => new
                {
                    CounterpartiesId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CounterpartyTypesId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterpartyCounterpartyType", x => new { x.CounterpartiesId, x.CounterpartyTypesId });
                    table.ForeignKey(
                        name: "FK_CounterpartyCounterpartyType_Counterparties_CounterpartiesId",
                        column: x => x.CounterpartiesId,
                        principalSchema: "UserData",
                        principalTable: "Counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CounterpartyCounterpartyType_CounterpartyTypes_CounterpartyTypesId",
                        column: x => x.CounterpartyTypesId,
                        principalSchema: "UserData",
                        principalTable: "CounterpartyTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CounterpartyUser",
                schema: "UserData",
                columns: table => new
                {
                    CounterpartiesId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    UsersId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterpartyUser", x => new { x.CounterpartiesId, x.UsersId });
                    table.ForeignKey(
                        name: "FK_CounterpartyUser_AspNetUsers_UsersId",
                        column: x => x.UsersId,
                        principalSchema: "UserData",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CounterpartyUser_Counterparties_CounterpartiesId",
                        column: x => x.CounterpartiesId,
                        principalSchema: "UserData",
                        principalTable: "Counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowExecutions",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReviewDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AnalystName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CounterpartyId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    JobId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Variables = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsBaseline = table.Column<bool>(type: "bit", nullable: false),
                    WorkflowState = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureDetail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WarningKind = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    WarningDetail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TemplateVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TemplateContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReplayUnmatchedJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FreezeSourceExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    FreezeSourceCaseId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    FreezeReplayedCount = table.Column<int>(type: "int", nullable: true),
                    FreezeEligibleCount = table.Column<int>(type: "int", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowExecutions_Counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalSchema: "UserData",
                        principalTable: "Counterparties",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FileMetadataId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    MediaType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Sources = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Topics = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TableOfContents = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DocumentCategoryId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Class = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OverviewPromptHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    WasPasswordProtected = table.Column<bool>(type: "bit", nullable: false),
                    HasMacros = table.Column<bool>(type: "bit", nullable: false),
                    ProcessingRequestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessingError = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_FileMetadata_FileMetadataId",
                        column: x => x.FileMetadataId,
                        principalSchema: "UserData",
                        principalTable: "FileMetadata",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Documents_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "UserData",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Json2XlsxTemplates",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ConversionType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileMetadataId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Json2XlsxTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Json2XlsxTemplates_FileMetadata_FileMetadataId",
                        column: x => x.FileMetadataId,
                        principalSchema: "UserData",
                        principalTable: "FileMetadata",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Json2XlsxTemplateVersions",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ConversionType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileMetadataId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Json2XlsxTemplateVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Json2XlsxTemplateVersions_FileMetadata_FileMetadataId",
                        column: x => x.FileMetadataId,
                        principalSchema: "UserData",
                        principalTable: "FileMetadata",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "EditorAgentChangeDecisions",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProposalId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ChangeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    MintedStableId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SavedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EditorAgentChangeDecisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EditorAgentChangeDecisions_EditorAgentProposals_ProposalId",
                        column: x => x.ProposalId,
                        principalSchema: "UserData",
                        principalTable: "EditorAgentProposals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentClasses",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    LlmDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Requirement = table.Column<int>(type: "int", nullable: false),
                    ShouldBeSingular = table.Column<bool>(type: "bit", nullable: false),
                    IsOutputDocument = table.Column<bool>(type: "bit", nullable: false),
                    DocumentCategoryId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PageOverviews = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentClasses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentClasses_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalSchema: "UserData",
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    GroupColor = table.Column<int>(type: "int", nullable: true),
                    TextInputTemplate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParameterizedConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpandedFromParameterizedKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StableSectionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SharedComponentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SharedComponentLinkState = table.Column<int>(type: "int", nullable: true),
                    SharedComponentVersionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SharedComponentBindings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SharedComponentSlotStates = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionState = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Configuration = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sections_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalSchema: "UserData",
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRegressionField",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Tolerance = table.Column<decimal>(type: "decimal(28,8)", precision: 28, scale: 8, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowRegressionField", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowRegressionField_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalSchema: "UserData",
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowSignatureField",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowSignatureField", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowSignatureField_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalSchema: "UserData",
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowVariable",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReplacementTag = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    IsHidden = table.Column<bool>(type: "bit", nullable: false),
                    IsConfidential = table.Column<bool>(type: "bit", nullable: false),
                    DefaultValue = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Options = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowVariable", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowVariable_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalSchema: "UserData",
                        principalTable: "Workflows",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowExecutionCounterparties",
                schema: "UserData",
                columns: table => new
                {
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CounterpartyId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowExecutionCounterparties", x => new { x.WorkflowExecutionId, x.CounterpartyId });
                    table.ForeignKey(
                        name: "FK_WorkflowExecutionCounterparties_Counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalSchema: "UserData",
                        principalTable: "Counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowExecutionCounterparties_WorkflowExecutions_WorkflowExecutionId",
                        column: x => x.WorkflowExecutionId,
                        principalSchema: "UserData",
                        principalTable: "WorkflowExecutions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowReports",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CounterpartyId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    OrganizationId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JobId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AnalystName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    WorkflowState = table.Column<int>(type: "int", nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowReports_Counterparties_CounterpartyId",
                        column: x => x.CounterpartyId,
                        principalSchema: "UserData",
                        principalTable: "Counterparties",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowReports_WorkflowExecutions_WorkflowExecutionId",
                        column: x => x.WorkflowExecutionId,
                        principalSchema: "UserData",
                        principalTable: "WorkflowExecutions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CounterpartyDocument",
                schema: "UserData",
                columns: table => new
                {
                    CounterpartiesId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DocumentsId = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterpartyDocument", x => new { x.CounterpartiesId, x.DocumentsId });
                    table.ForeignKey(
                        name: "FK_CounterpartyDocument_Counterparties_CounterpartiesId",
                        column: x => x.CounterpartiesId,
                        principalSchema: "UserData",
                        principalTable: "Counterparties",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CounterpartyDocument_Documents_DocumentsId",
                        column: x => x.DocumentsId,
                        principalSchema: "UserData",
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentInfos",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    JobId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ShouldUseOcr = table.Column<bool>(type: "bit", nullable: false),
                    ShouldBeDisplayedInOutput = table.Column<bool>(type: "bit", nullable: false),
                    DocumentClass = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExcludedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExclusionReason = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentInfos_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "UserData",
                        principalTable: "Documents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Pages",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    DocumentId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    EmbeddingVectorId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    PageNumber = table.Column<int>(type: "int", nullable: false),
                    FilePageNumber = table.Column<int>(type: "int", nullable: false),
                    SourceUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Location = table.Column<int>(type: "int", nullable: false),
                    RawText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Overview = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SectionTitles = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pages_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "UserData",
                        principalTable: "Documents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Pages_EmbeddingVectors_EmbeddingVectorId",
                        column: x => x.EmbeddingVectorId,
                        principalSchema: "UserData",
                        principalTable: "EmbeddingVectors",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TokenUsages",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SectionId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    BlockId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    DocumentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PageId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    UsageType = table.Column<int>(type: "int", nullable: false),
                    ExecutionContext = table.Column<int>(type: "int", nullable: false),
                    BlockType = table.Column<int>(type: "int", nullable: true),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Family = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ModelId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Duration = table.Column<TimeSpan>(type: "time", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Metadata = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ModelPricingId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenUsages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TokenUsages_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalSchema: "UserData",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TokenUsages_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalSchema: "UserData",
                        principalTable: "Documents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TokenUsages_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalSchema: "UserData",
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TokenUsages_WorkflowExecutions_WorkflowExecutionId",
                        column: x => x.WorkflowExecutionId,
                        principalSchema: "UserData",
                        principalTable: "WorkflowExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Blocks",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BlockResponseId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SectionId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    ReplacementTag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OutputTitleTemplate = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    SupportedDocumentClasses = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsOutputSource = table.Column<bool>(type: "bit", nullable: false),
                    ShouldUpdateCounterpartyWithResult = table.Column<bool>(type: "bit", nullable: false),
                    UpdateCounterpartyPropertyKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RunsAlone = table.Column<bool>(type: "bit", nullable: false),
                    CounterpartyResultType = table.Column<int>(type: "int", nullable: true),
                    ShouldDisplayInLogs = table.Column<bool>(type: "bit", nullable: false),
                    Properties = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RegressionSchema = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StableBlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SharedComponentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SharedComponentLinkState = table.Column<int>(type: "int", nullable: true),
                    SharedComponentVersionHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SharedComponentBindings = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionState = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Blocks_Sections_SectionId",
                        column: x => x.SectionId,
                        principalSchema: "UserData",
                        principalTable: "Sections",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ReportSections",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowReportId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Layout = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExcludeFromWordExport = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportSections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportSections_WorkflowReports_WorkflowReportId",
                        column: x => x.WorkflowReportId,
                        principalSchema: "UserData",
                        principalTable: "WorkflowReports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TokenUsageComponents",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    TokenUsageId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ComponentType = table.Column<int>(type: "int", nullable: false),
                    Units = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,10)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TokenUsageComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TokenUsageComponents_TokenUsages_TokenUsageId",
                        column: x => x.TokenUsageId,
                        principalSchema: "UserData",
                        principalTable: "TokenUsages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentRunOutputs",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    RunExecutionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Output = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CitationFactsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRunOutputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRunOutputs_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "UserData",
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentRunSessionBundles",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ConversationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Round = table.Column<int>(type: "int", nullable: false),
                    RunExecutionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    Segment = table.Column<int>(type: "int", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageProvider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StorageKey = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    VersionStamp = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    DroppedOutFileCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRunSessionBundles", x => x.Id);
                    table.CheckConstraint("CK_AgentRunSessionBundles_ExactlyOneOwner", "([BlockId] IS NOT NULL AND [ConversationId] IS NULL) OR ([BlockId] IS NULL AND [ConversationId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_AgentRunSessionBundles_AgentConversations_ConversationId",
                        column: x => x.ConversationId,
                        principalSchema: "UserData",
                        principalTable: "AgentConversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentRunSessionBundles_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "UserData",
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentRunTraceFiles",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    RunExecutionId = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Attempt = table.Column<int>(type: "int", nullable: false),
                    Path = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRunTraceFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRunTraceFiles_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "UserData",
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockCitationTraces",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockCitationTraces", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockCitationTraces_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "UserData",
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockResponses",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    RawResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JsonResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HtmlResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MarkdownResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DebugInfo = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RegressionFacts = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BinaryResponse = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    MediaType = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BlockResponses_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "UserData",
                        principalTable: "Blocks",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RagSettings",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    ExactIndexesInput = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    NumberOfPagesInput = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    StartIndexInput = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    EndIndexInput = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    PageAdjustmentInput = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    TopicsQuery = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TableOfContentsInput = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QueryTemplate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IterationLimit = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ContextLimit = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Keywords = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SmartFilterExpressions = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ShouldFilterKeyWordsExclusively = table.Column<bool>(type: "bit", nullable: false),
                    ShouldSearchOverviews = table.Column<bool>(type: "bit", nullable: false),
                    ShouldIncludeNeighboringPages = table.Column<bool>(type: "bit", nullable: false),
                    ExecutionState = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RagSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RagSettings_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "UserData",
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TextInputs",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ReplacementTag = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExecutionState = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextInputs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TextInputs_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "UserData",
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReportParagraphs",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SectionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Subtitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Confidence = table.Column<double>(type: "float", nullable: true),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReplacementTag = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sources = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    Citations = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CitedSourcesByBlock = table.Column<string>(type: "nvarchar(max)", nullable: false, defaultValueSql: "N'[]'"),
                    ChartPayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TablePayload = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportParagraphs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportParagraphs_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "UserData",
                        principalTable: "Blocks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ReportParagraphs_ReportSections_SectionId",
                        column: x => x.SectionId,
                        principalSchema: "UserData",
                        principalTable: "ReportSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowExecutionLogs",
                schema: "UserData",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SectionName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlockName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlockType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReplacementTag = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Inputs = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Prompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Output = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfidenceScore = table.Column<double>(type: "float", nullable: true),
                    Sources = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Error = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Duration = table.Column<TimeSpan>(type: "time", nullable: true),
                    ModelUsed = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TemplatePrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ShouldDisplayInLogs = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WorkflowExecutionId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    WorkflowId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    BlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ReportParagraphId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowExecutionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowExecutionLogs_Blocks_BlockId",
                        column: x => x.BlockId,
                        principalSchema: "UserData",
                        principalTable: "Blocks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_WorkflowExecutionLogs_ReportParagraphs_ReportParagraphId",
                        column: x => x.ReportParagraphId,
                        principalSchema: "UserData",
                        principalTable: "ReportParagraphs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkflowExecutionLogs_WorkflowExecutions_WorkflowExecutionId",
                        column: x => x.WorkflowExecutionId,
                        principalSchema: "UserData",
                        principalTable: "WorkflowExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkflowExecutionLogs_Workflows_WorkflowId",
                        column: x => x.WorkflowId,
                        principalSchema: "UserData",
                        principalTable: "Workflows",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentConversations_EndedAt",
                schema: "UserData",
                table: "AgentConversations",
                column: "EndedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AgentConversations_LastActivityAt",
                schema: "UserData",
                table: "AgentConversations",
                column: "LastActivityAt");

            migrationBuilder.CreateIndex(
                name: "IX_AgentConversations_UserId_Purpose_SubjectId",
                schema: "UserData",
                table: "AgentConversations",
                columns: new[] { "UserId", "Purpose", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentConversationTurns_ConversationId_Number",
                schema: "UserData",
                table: "AgentConversationTurns",
                columns: new[] { "ConversationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentConversationTurns_Status_HostInstanceId",
                schema: "UserData",
                table: "AgentConversationTurns",
                columns: new[] { "Status", "HostInstanceId" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentConversationTurns_UserId_Status",
                schema: "UserData",
                table: "AgentConversationTurns",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunOutputs_BlockId_Round",
                schema: "UserData",
                table: "AgentRunOutputs",
                columns: new[] { "BlockId", "Round" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunOutputs_RunExecutionId",
                schema: "UserData",
                table: "AgentRunOutputs",
                column: "RunExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunSessionBundles_BlockId_Round",
                schema: "UserData",
                table: "AgentRunSessionBundles",
                columns: new[] { "BlockId", "Round" },
                unique: true,
                filter: "[BlockId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunSessionBundles_ConversationId_Round",
                schema: "UserData",
                table: "AgentRunSessionBundles",
                columns: new[] { "ConversationId", "Round" },
                unique: true,
                filter: "[ConversationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunSessionBundles_CreatedAt",
                schema: "UserData",
                table: "AgentRunSessionBundles",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunSessionBundles_RunExecutionId",
                schema: "UserData",
                table: "AgentRunSessionBundles",
                column: "RunExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunTraceFiles_BlockId",
                schema: "UserData",
                table: "AgentRunTraceFiles",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunTraceFiles_RunExecutionId",
                schema: "UserData",
                table: "AgentRunTraceFiles",
                column: "RunExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRunTraceFiles_RunExecutionId_Attempt_Path",
                schema: "UserData",
                table: "AgentRunTraceFiles",
                columns: new[] { "RunExecutionId", "Attempt", "Path" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                schema: "UserData",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                schema: "UserData",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true,
                filter: "[NormalizedName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                schema: "UserData",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                schema: "UserData",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                schema: "UserData",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                schema: "UserData",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_OrganizationId",
                schema: "UserData",
                table: "AspNetUsers",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                schema: "UserData",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Authorizations_ApplicationId",
                schema: "UserData",
                table: "Authorizations",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockCitationTraces_BlockId",
                schema: "UserData",
                table: "BlockCitationTraces",
                column: "BlockId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockResponses_BlockId",
                schema: "UserData",
                table: "BlockResponses",
                column: "BlockId",
                unique: true,
                filter: "[BlockId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_SectionId",
                schema: "UserData",
                table: "Blocks",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversionTypes_OrganizationId_Name_IsDeleted",
                schema: "UserData",
                table: "ConversionTypes",
                columns: new[] { "OrganizationId", "Name", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Counterparties_OrganizationId",
                schema: "UserData",
                table: "Counterparties",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Counterparties_ParentId",
                schema: "UserData",
                table: "Counterparties",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_CounterpartyCounterpartyType_CounterpartyTypesId",
                schema: "UserData",
                table: "CounterpartyCounterpartyType",
                column: "CounterpartyTypesId");

            migrationBuilder.CreateIndex(
                name: "IX_CounterpartyDocument_DocumentsId",
                schema: "UserData",
                table: "CounterpartyDocument",
                column: "DocumentsId");

            migrationBuilder.CreateIndex(
                name: "IX_CounterpartyUser_UsersId",
                schema: "UserData",
                table: "CounterpartyUser",
                column: "UsersId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentClasses_WorkflowId",
                schema: "UserData",
                table: "DocumentClasses",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentInfos_DocumentId",
                schema: "UserData",
                table: "DocumentInfos",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentInfos_JobId",
                schema: "UserData",
                table: "DocumentInfos",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_FileMetadataId",
                schema: "UserData",
                table: "Documents",
                column: "FileMetadataId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OrganizationId",
                schema: "UserData",
                table: "Documents",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_WorkflowTypeId",
                schema: "UserData",
                table: "Documents",
                column: "WorkflowTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentUnlockAuditLogs_OrganizationId_CreatedAt",
                schema: "UserData",
                table: "DocumentUnlockAuditLogs",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EditorAgentChangeDecisions_ProposalId_ChangeId",
                schema: "UserData",
                table: "EditorAgentChangeDecisions",
                columns: new[] { "ProposalId", "ChangeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorAgentDrafts_ConversationId_Revision",
                schema: "UserData",
                table: "EditorAgentDrafts",
                columns: new[] { "ConversationId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorAgentProposals_ConversationId",
                schema: "UserData",
                table: "EditorAgentProposals",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorAgentProposals_TurnId",
                schema: "UserData",
                table: "EditorAgentProposals",
                column: "TurnId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorAgentSpendUnlocks_OrganizationId_Day_FromBatches",
                schema: "UserData",
                table: "EditorAgentSpendUnlocks",
                columns: new[] { "OrganizationId", "Day", "FromBatches" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EditorAgentTurnFeedback_ConversationId",
                schema: "UserData",
                table: "EditorAgentTurnFeedback",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_EditorAgentTurnReports_ConversationId",
                schema: "UserData",
                table: "EditorAgentTurnReports",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_FileMetadata_OrganizationId",
                schema: "UserData",
                table: "FileMetadata",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_HedgeFundsMonitoringObservationAudits_HedgeFundsMonitoringObservationId",
                schema: "UserData",
                table: "HedgeFundsMonitoringObservationAudits",
                column: "HedgeFundsMonitoringObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_Json2XlsxTemplates_FileMetadataId",
                schema: "UserData",
                table: "Json2XlsxTemplates",
                column: "FileMetadataId");

            migrationBuilder.CreateIndex(
                name: "IX_Json2XlsxTemplates_OrganizationId_ConversionType",
                schema: "UserData",
                table: "Json2XlsxTemplates",
                columns: new[] { "OrganizationId", "ConversionType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Json2XlsxTemplateVersions_FileMetadataId",
                schema: "UserData",
                table: "Json2XlsxTemplateVersions",
                column: "FileMetadataId");

            migrationBuilder.CreateIndex(
                name: "IX_Json2XlsxTemplateVersions_OrganizationId_ConversionType",
                schema: "UserData",
                table: "Json2XlsxTemplateVersions",
                columns: new[] { "OrganizationId", "ConversionType" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationCleanupSettings_OrganizationId",
                schema: "UserData",
                table: "OrganizationCleanupSettings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationModelSettings_IsEnabled_UsageContext",
                schema: "UserData",
                table: "OrganizationModelSettings",
                columns: new[] { "IsEnabled", "UsageContext" });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationModelSettings_OrganizationId",
                schema: "UserData",
                table: "OrganizationModelSettings",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationModelSettings_OrgId_UsageContext",
                schema: "UserData",
                table: "OrganizationModelSettings",
                columns: new[] { "OrganizationId", "UsageContext" },
                unique: true,
                filter: "[OrganizationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_DocumentId",
                schema: "UserData",
                table: "Pages",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Pages_EmbeddingVectorId",
                schema: "UserData",
                table: "Pages",
                column: "EmbeddingVectorId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateEquityMonitoringAllocations_PrivateEquityMonitoringObservationId",
                schema: "UserData",
                table: "PrivateEquityMonitoringAllocations",
                column: "PrivateEquityMonitoringObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateEquityMonitoringObservationAudits_PrivateEquityMonitoringObservationId",
                schema: "UserData",
                table: "PrivateEquityMonitoringObservationAudits",
                column: "PrivateEquityMonitoringObservationId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateEquityMonitoringObservations_FundId",
                schema: "UserData",
                table: "PrivateEquityMonitoringObservations",
                column: "FundId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateEquityMonitoringObservations_JobId",
                schema: "UserData",
                table: "PrivateEquityMonitoringObservations",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateEquityMonitoringObservations_OrganizationId_IsDeleted",
                schema: "UserData",
                table: "PrivateEquityMonitoringObservations",
                columns: new[] { "OrganizationId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_RagSettings_BlockId",
                schema: "UserData",
                table: "RagSettings",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_RefinementPresets_OrganizationId_IsDeleted_UserId",
                schema: "UserData",
                table: "RefinementPresets",
                columns: new[] { "OrganizationId", "IsDeleted", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportFeedbackItems_OrganizationId_WorkflowExecutionId",
                schema: "UserData",
                table: "ReportFeedbackItems",
                columns: new[] { "OrganizationId", "WorkflowExecutionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportFeedbackItems_WorkflowTypeId_Status",
                schema: "UserData",
                table: "ReportFeedbackItems",
                columns: new[] { "WorkflowTypeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportParagraphs_BlockId",
                schema: "UserData",
                table: "ReportParagraphs",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportParagraphs_SectionId",
                schema: "UserData",
                table: "ReportParagraphs",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportSections_WorkflowReportId",
                schema: "UserData",
                table: "ReportSections",
                column: "WorkflowReportId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningBacktestDocuments_BacktestId_Sequence",
                schema: "UserData",
                table: "ScreeningBacktestDocuments",
                columns: new[] { "BacktestId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningBacktestDocuments_DocumentId",
                schema: "UserData",
                table: "ScreeningBacktestDocuments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningBacktestRuns_BacktestId_Sequence",
                schema: "UserData",
                table: "ScreeningBacktestRuns",
                columns: new[] { "BacktestId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningBacktests_Status",
                schema: "UserData",
                table: "ScreeningBacktests",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningBacktests_WorkflowTypeId_CreatedAt",
                schema: "UserData",
                table: "ScreeningBacktests",
                columns: new[] { "WorkflowTypeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningRecords_CreatedAt",
                schema: "UserData",
                table: "ScreeningRecords",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningRecords_DocumentId",
                schema: "UserData",
                table: "ScreeningRecords",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningRecords_OrganizationId_CreatedAt",
                schema: "UserData",
                table: "ScreeningRecords",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningRecords_OrganizationId_WorkflowTypeId_CreatedAt",
                schema: "UserData",
                table: "ScreeningRecords",
                columns: new[] { "OrganizationId", "WorkflowTypeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningRecords_OriginatingDocumentId",
                schema: "UserData",
                table: "ScreeningRecords",
                column: "OriginatingDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ScreeningRecords_WorkflowExecutionId",
                schema: "UserData",
                table: "ScreeningRecords",
                column: "WorkflowExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_WorkflowId",
                schema: "UserData",
                table: "Sections",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_TextInputs_BlockId",
                schema: "UserData",
                table: "TextInputs",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_ApplicationId",
                schema: "UserData",
                table: "Tokens",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_Tokens_AuthorizationId",
                schema: "UserData",
                table: "Tokens",
                column: "AuthorizationId");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsageComponents_ComponentType",
                schema: "UserData",
                table: "TokenUsageComponents",
                column: "ComponentType");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsageComponents_TokenUsageId",
                schema: "UserData",
                table: "TokenUsageComponents",
                column: "TokenUsageId");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsageComponents_TokenUsageId_ComponentType",
                schema: "UserData",
                table: "TokenUsageComponents",
                columns: new[] { "TokenUsageId", "ComponentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_DocumentId",
                schema: "UserData",
                table: "TokenUsages",
                column: "DocumentId",
                filter: "[DocumentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_OrganizationId_BlockType_CreatedAt",
                schema: "UserData",
                table: "TokenUsages",
                columns: new[] { "OrganizationId", "BlockType", "CreatedAt" },
                filter: "[BlockType] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_OrganizationId_CreatedAt",
                schema: "UserData",
                table: "TokenUsages",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_OrganizationId_ExecutionContext_CreatedAt",
                schema: "UserData",
                table: "TokenUsages",
                columns: new[] { "OrganizationId", "ExecutionContext", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_OrganizationId_UsageType_CreatedAt",
                schema: "UserData",
                table: "TokenUsages",
                columns: new[] { "OrganizationId", "UsageType", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_UserId_CreatedAt",
                schema: "UserData",
                table: "TokenUsages",
                columns: new[] { "UserId", "CreatedAt" },
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TokenUsages_WorkflowExecutionId",
                schema: "UserData",
                table: "TokenUsages",
                column: "WorkflowExecutionId",
                filter: "[WorkflowExecutionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserOrganizations_OrganizationId",
                schema: "UserData",
                table: "UserOrganizations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOrganizations_OrganizationId_IsActive",
                schema: "UserData",
                table: "UserOrganizations",
                columns: new[] { "OrganizationId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserOrganizations_UserId",
                schema: "UserData",
                table: "UserOrganizations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserOrganizations_UserId_IsActive",
                schema: "UserData",
                table: "UserOrganizations",
                columns: new[] { "UserId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_UserTrustedDevices_TrustedUntil",
                schema: "UserData",
                table: "UserTrustedDevices",
                column: "TrustedUntil");

            migrationBuilder.CreateIndex(
                name: "IX_UserTrustedDevices_UserId_DeviceFingerprint",
                schema: "UserData",
                table: "UserTrustedDevices",
                columns: new[] { "UserId", "DeviceFingerprint" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationCases_BaselineExecutionId",
                schema: "UserData",
                table: "ValidationCases",
                column: "BaselineExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationCases_OrganizationId_SourceInstance_SourceCaseId",
                schema: "UserData",
                table: "ValidationCases",
                columns: new[] { "OrganizationId", "SourceInstance", "SourceCaseId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationCases_OrganizationId_State",
                schema: "UserData",
                table: "ValidationCases",
                columns: new[] { "OrganizationId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationCases_SchemaId",
                schema: "UserData",
                table: "ValidationCases",
                column: "SchemaId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationReferenceAuditLogs_OrganizationId_Action_CreatedAt",
                schema: "UserData",
                table: "ValidationReferenceAuditLogs",
                columns: new[] { "OrganizationId", "Action", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationReferenceAuditLogs_OrganizationId_CaseId_CreatedAt",
                schema: "UserData",
                table: "ValidationReferenceAuditLogs",
                columns: new[] { "OrganizationId", "CaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_AwaitingTrim",
                schema: "UserData",
                table: "ValidationRuns",
                columns: new[] { "CreatedAt", "Id" },
                descending: new[] { true, false },
                filter: "[PayloadTrimmedAt] IS NULL AND [Status] = 2 AND [Outcome] = 0 AND [UnsettledFieldCount] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_CaseId_CreatedAt",
                schema: "UserData",
                table: "ValidationRuns",
                columns: new[] { "CaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_OrganizationId_ActTargetKind_ActTargetId_CreatedAt_BatchId",
                schema: "UserData",
                table: "ValidationRuns",
                columns: new[] { "OrganizationId", "ActTargetKind", "ActTargetId", "CreatedAt", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_OrganizationId_BatchId",
                schema: "UserData",
                table: "ValidationRuns",
                columns: new[] { "OrganizationId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_OrganizationId_CandidateSourceId",
                schema: "UserData",
                table: "ValidationRuns",
                columns: new[] { "OrganizationId", "CandidateSourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_OrganizationId_CreatedAt",
                schema: "UserData",
                table: "ValidationRuns",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_OrganizationId_SuiteId_CreatedAt_BatchId",
                schema: "UserData",
                table: "ValidationRuns",
                columns: new[] { "OrganizationId", "SuiteId", "CreatedAt", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationRuns_Status_CreatedAt",
                schema: "UserData",
                table: "ValidationRuns",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationSchedules_OrganizationId_TargetKind_TargetId",
                schema: "UserData",
                table: "ValidationSchedules",
                columns: new[] { "OrganizationId", "TargetKind", "TargetId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ValidationSuites_OrganizationId_SourceInstance_SourceSuiteId",
                schema: "UserData",
                table: "ValidationSuites",
                columns: new[] { "OrganizationId", "SourceInstance", "SourceSuiteId" });

            migrationBuilder.CreateIndex(
                name: "IX_ValidationThresholdSettings_OrganizationId",
                schema: "UserData",
                table: "ValidationThresholdSettings",
                column: "OrganizationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationCriterionRecords_OrganizationId_JudgedAtUtc",
                schema: "UserData",
                table: "VerificationCriterionRecords",
                columns: new[] { "OrganizationId", "JudgedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationCriterionRecords_OrganizationId_TemplateVersionId_JudgedAtUtc",
                schema: "UserData",
                table: "VerificationCriterionRecords",
                columns: new[] { "OrganizationId", "TemplateVersionId", "JudgedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationCriterionRecords_OrganizationId_WorkflowTypeId_CriterionId_JudgedAtUtc",
                schema: "UserData",
                table: "VerificationCriterionRecords",
                columns: new[] { "OrganizationId", "WorkflowTypeId", "CriterionId", "JudgedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VerificationCriterionRecords_RunExecutionId",
                schema: "UserData",
                table: "VerificationCriterionRecords",
                column: "RunExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_VerificationCriterionRecords_WorkflowExecutionId",
                schema: "UserData",
                table: "VerificationCriterionRecords",
                column: "WorkflowExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBaselineRegistrations_OrganizationId_WorkflowTypeId",
                schema: "UserData",
                table: "WorkflowBaselineRegistrations",
                columns: new[] { "OrganizationId", "WorkflowTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowBaselineRegistrations_OrganizationId_WorkflowTypeId_CounterpartyId",
                schema: "UserData",
                table: "WorkflowBaselineRegistrations",
                columns: new[] { "OrganizationId", "WorkflowTypeId", "CounterpartyId" },
                unique: true,
                filter: "[State] = 0 AND [IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionCounterparties_CounterpartyId",
                schema: "UserData",
                table: "WorkflowExecutionCounterparties",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionLogs_BlockId",
                schema: "UserData",
                table: "WorkflowExecutionLogs",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionLogs_ReportParagraphId",
                schema: "UserData",
                table: "WorkflowExecutionLogs",
                column: "ReportParagraphId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionLogs_WorkflowExecutionId",
                schema: "UserData",
                table: "WorkflowExecutionLogs",
                column: "WorkflowExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutionLogs_WorkflowId",
                schema: "UserData",
                table: "WorkflowExecutionLogs",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_CounterpartyId",
                schema: "UserData",
                table: "WorkflowExecutions",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowExecutions_IdempotencyKey_OrganizationId",
                schema: "UserData",
                table: "WorkflowExecutions",
                columns: new[] { "IdempotencyKey", "OrganizationId" },
                unique: true,
                filter: "[IdempotencyKey] IS NOT NULL AND [OrganizationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRegressionBlockResults_RunId",
                schema: "UserData",
                table: "WorkflowRegressionBlockResults",
                column: "WorkflowRegressionRunId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRegressionField_WorkflowId",
                schema: "UserData",
                table: "WorkflowRegressionField",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRegressionRuns_BaselineExecutionId",
                schema: "UserData",
                table: "WorkflowRegressionRuns",
                column: "BaselineExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRegressionRuns_OrgId_BatchId",
                schema: "UserData",
                table: "WorkflowRegressionRuns",
                columns: new[] { "OrganizationId", "BatchId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRegressionRuns_OrgId_Status",
                schema: "UserData",
                table: "WorkflowRegressionRuns",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowReports_CounterpartyId",
                schema: "UserData",
                table: "WorkflowReports",
                column: "CounterpartyId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowReports_WorkflowExecutionId",
                schema: "UserData",
                table: "WorkflowReports",
                column: "WorkflowExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_OrganizationId",
                schema: "UserData",
                table: "Workflows",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_UserId",
                schema: "UserData",
                table: "Workflows",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSignatureField_WorkflowId",
                schema: "UserData",
                table: "WorkflowSignatureField",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowVariable_WorkflowId",
                schema: "UserData",
                table: "WorkflowVariable",
                column: "WorkflowId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentRunOutputs",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AgentRunSessionBundles",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AgentRunTraceFiles",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AspNetRoleClaims",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "BlockCitationTraces",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "BlockResponses",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ConversionTypes",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "CounterpartyCounterpartyType",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "CounterpartyDocument",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "CounterpartyUser",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "DocumentClasses",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "DocumentInfos",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "DocumentUnlockAuditLogs",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "EditorAgentChangeDecisions",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "EditorAgentDrafts",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "EditorAgentSpendUnlocks",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "EditorAgentTurnFeedback",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "EditorAgentTurnReports",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Files",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "graph_edges",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "graph_nodes",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "HedgeFundsMonitoringObservationAudits",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "HedgeFundsMonthlySummaries",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Json2XlsxTemplates",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Json2XlsxTemplateVersions",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "OrganizationCleanupSettings",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "OrganizationModelSettings",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Pages",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "PrivateEquityMonitoringAllocations",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "PrivateEquityMonitoringObservationAudits",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "RagSettings",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "RefinementPresets",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "RegressionTestResults",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ReportFeedbackItems",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Scopes",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ScreeningBacktestDocuments",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ScreeningBacktestRuns",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ScreeningBacktests",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ScreeningRecords",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "TextInputs",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Tokens",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "TokenUsageComponents",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "UserOrganizations",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "UserTrustedDevices",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "UserWorkflowTypeVisibilities",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ValidationReferenceAuditLogs",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ValidationRuns",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ValidationSchedules",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ValidationSuites",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ValidationThresholdSettings",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "VerificationCriterionRecords",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "WorkflowBaselineRegistrations",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "WorkflowExecutionCounterparties",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "WorkflowExecutionLogs",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "WorkflowRegressionBlockResults",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "WorkflowRegressionField",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "WorkflowSignatureField",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "WorkflowVariable",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AspNetRoles",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "CounterpartyTypes",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "EditorAgentProposals",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "HedgeFundsMonitoringObservations",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "EmbeddingVectors",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "PrivateEquityMonitoringObservations",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Authorizations",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "TokenUsages",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ValidationCases",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ReportParagraphs",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "WorkflowRegressionRuns",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AgentConversationTurns",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Applications",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Documents",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Blocks",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "ReportSections",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AgentConversations",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "FileMetadata",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Sections",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "WorkflowReports",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Workflows",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "WorkflowExecutions",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "AspNetUsers",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Counterparties",
                schema: "UserData");

            migrationBuilder.DropTable(
                name: "Organizations",
                schema: "UserData");
        }
    }
}

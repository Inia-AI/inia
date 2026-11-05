using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IniaAi.Migrations.Mssql.Core
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Core");

            migrationBuilder.CreateTable(
                name: "DocumentCategories",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
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
                    table.PrimaryKey("PK_DocumentCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExcelAnalysisPrompts",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LayoutAnalysisSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LayoutAnalysisUserPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeaderDatabaseSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeaderDatabaseUserPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeaderMultiNoSharedSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeaderMultiNoSharedUserPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeaderMultiSharedSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HeaderMultiSharedUserPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExcelAnalysisPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GraphRagPrompts",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserPromptTemplate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MaxDepthWarning = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GraphRagPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelPricings",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Family = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ModelId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ModelAlias = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PricingType = table.Column<int>(type: "int", nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelPricings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PdfOcrPrompts",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TranscriptionSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TranscriptionPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JudgeSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JudgePrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PdfOcrPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PendingSignatureOverrides",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PlaceholderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ProposedValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ProposerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingSignatureOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReconciliationPrompts",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FindMatchPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HedgeFundsMonitoringFindMatchPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CompareListsPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    HedgeFundsMonitoringCompareListsPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScreeningPrompts",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    JudgeSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JudgePrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreeningPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SharedComponentCategories",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedComponentCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SharedComponents",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CategoryId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedComponents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SharedComponentVersions",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SharedComponentId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    SequenceNumber = table.Column<int>(type: "int", nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ParentVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ContentJsonCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    PayloadAbsentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedComponentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SharedComponentVersions_SharedComponentVersions_ParentVersionId",
                        column: x => x.ParentVersionId,
                        principalSchema: "Core",
                        principalTable: "SharedComponentVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SignatureFieldOverrideAuditLogs",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    PlaceholderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    PreviousValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ActorUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    ActorUserEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ApproversJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReferencedBlockNamesJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureFieldOverrideAuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SignatureFieldOverrides",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PlaceholderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureFieldOverrides", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TemplateCategories",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ParentCategoryId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
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
                    table.PrimaryKey("PK_TemplateCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TemplateCategories_TemplateCategories_ParentCategoryId",
                        column: x => x.ParentCategoryId,
                        principalSchema: "Core",
                        principalTable: "TemplateCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TopicsPrompts",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TopicsAnalysisSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TopicsAnalysisUserPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TopicGroupingSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TopicGroupingUserPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TopicsPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UseAutoDetectedTableOfContentsPrompts",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    RelevantSectionsIdentificationPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RelevanceAssessmentPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PageSelectionPrompt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UseAutoDetectedTableOfContentsPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValidationPrompts",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ExtractionSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExtractionPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JudgeSystemPrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    JudgePrompt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationPrompts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ValidationSchemas",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IsPublic = table.Column<bool>(type: "bit", nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    SchemaJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SourceInstance = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SourceSchemaId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ValidationSchemas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTypeCategories",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTypeCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowVersions",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ContentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ParentVersionId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    WorkflowJsonCompressed = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    PayloadTrimmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AutoNarrativeSummary = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActivityScriptJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    HumanNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowVersions_WorkflowVersions_ParentVersionId",
                        column: x => x.ParentVersionId,
                        principalSchema: "Core",
                        principalTable: "WorkflowVersions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ModelPricingComponents",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ModelPricingId = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: false),
                    ComponentType = table.Column<int>(type: "int", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,6)", nullable: false),
                    Unit = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    Currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false, defaultValue: "USD"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelPricingComponents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelPricingComponents_ModelPricings_ModelPricingId",
                        column: x => x.ModelPricingId,
                        principalSchema: "Core",
                        principalTable: "ModelPricings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SignatureOverrideVotes",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PendingOverrideId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    VoterUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    VoteKind = table.Column<int>(type: "int", nullable: false),
                    Comment = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SuggestedValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureOverrideVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignatureOverrideVotes_PendingSignatureOverrides_PendingOverrideId",
                        column: x => x.PendingOverrideId,
                        principalSchema: "Core",
                        principalTable: "PendingSignatureOverrides",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTypes",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CategoryId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    DuplicatedFromWorkflowTypeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTypes_WorkflowTypeCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "Core",
                        principalTable: "WorkflowTypeCategories",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Workflows",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OrganizationId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Configuration = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TextInputTemplate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ParameterizedConfig = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpandedFromParameterizedKey = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                        name: "FK_Workflows_WorkflowTypes_WorkflowTypeId",
                        column: x => x.WorkflowTypeId,
                        principalSchema: "Core",
                        principalTable: "WorkflowTypes",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTypeCounterpartyTypeMap",
                schema: "Core",
                columns: table => new
                {
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CounterpartyTypeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTypeCounterpartyTypeMap", x => new { x.WorkflowTypeId, x.CounterpartyTypeId });
                    table.ForeignKey(
                        name: "FK_WorkflowTypeCounterpartyTypeMap_WorkflowTypes_WorkflowTypeId",
                        column: x => x.WorkflowTypeId,
                        principalSchema: "Core",
                        principalTable: "WorkflowTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTypeOrganizationAccess",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_WorkflowTypeOrganizationAccess", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTypeOrganizationAccess_WorkflowTypes_WorkflowTypeId",
                        column: x => x.WorkflowTypeId,
                        principalSchema: "Core",
                        principalTable: "WorkflowTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentClasses",
                schema: "Core",
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
                        principalSchema: "Core",
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                schema: "Core",
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
                        principalSchema: "Core",
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowRegressionField",
                schema: "Core",
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
                        principalSchema: "Core",
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowSignatureField",
                schema: "Core",
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
                        principalSchema: "Core",
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowVariable",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    WorkflowId = table.Column<string>(type: "nvarchar(450)", nullable: false),
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
                        principalSchema: "Core",
                        principalTable: "Workflows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Blocks",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
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
                        principalSchema: "Core",
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BlockTemplate",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TemplateCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BlockType = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TemplateScope = table.Column<int>(type: "int", nullable: false),
                    CategoryId = table.Column<string>(type: "nvarchar(100)", nullable: false),
                    WorkflowTypeId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    DocumentCategoryId = table.Column<string>(type: "nvarchar(100)", nullable: true),
                    ParentTemplateId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    TemplateVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    TemplateUsageCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsSystemTemplate = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    IsPublicTemplate = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockTemplate", x => x.Id);
                    table.CheckConstraint("CK_BlockTemplate_SystemTemplate", "([IsSystemTemplate] = 0) OR ([IsSystemTemplate] = 1 AND [IsPublicTemplate] = 1 AND [OrganizationId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_BlockTemplate_BlockTemplate_ParentTemplateId",
                        column: x => x.ParentTemplateId,
                        principalSchema: "Core",
                        principalTable: "BlockTemplate",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BlockTemplate_Blocks_Id",
                        column: x => x.Id,
                        principalSchema: "Core",
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BlockTemplate_DocumentCategories_DocumentCategoryId",
                        column: x => x.DocumentCategoryId,
                        principalSchema: "Core",
                        principalTable: "DocumentCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BlockTemplate_TemplateCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalSchema: "Core",
                        principalTable: "TemplateCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RagSettings",
                schema: "Core",
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
                        principalSchema: "Core",
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TextInputs",
                schema: "Core",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BlockId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ReplacementTag = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(max)", nullable: true),
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
                        principalSchema: "Core",
                        principalTable: "Blocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_SectionId",
                schema: "Core",
                table: "Blocks",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Blocks_SharedComponentId",
                schema: "Core",
                table: "Blocks",
                column: "SharedComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockTemplate_CategoryId",
                schema: "Core",
                table: "BlockTemplate",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockTemplate_CreatedAt",
                schema: "Core",
                table: "BlockTemplate",
                column: "CreatedAt",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_BlockTemplate_CreatedByUserId",
                schema: "Core",
                table: "BlockTemplate",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockTemplate_DocumentCategoryId",
                schema: "Core",
                table: "BlockTemplate",
                column: "DocumentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockTemplate_ParentTemplateId",
                schema: "Core",
                table: "BlockTemplate",
                column: "ParentTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_BlockTemplate_Search",
                schema: "Core",
                table: "BlockTemplate",
                columns: new[] { "TemplateScope", "BlockType", "OrganizationId", "IsPublicTemplate", "CategoryId" })
                .Annotation("SqlServer:Include", new[] { "TemplateUsageCount", "WorkflowTypeId", "DocumentCategoryId", "CreatedByUserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BlockTemplate_TemplateCode",
                schema: "Core",
                table: "BlockTemplate",
                column: "TemplateCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockTemplate_TemplateScope",
                schema: "Core",
                table: "BlockTemplate",
                column: "TemplateScope");

            migrationBuilder.CreateIndex(
                name: "IX_BlockTemplate_WorkflowTypeId",
                schema: "Core",
                table: "BlockTemplate",
                column: "WorkflowTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCategories_DisplayOrder",
                schema: "Core",
                table: "DocumentCategories",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentCategories_Name",
                schema: "Core",
                table: "DocumentCategories",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentClasses_WorkflowId",
                schema: "Core",
                table: "DocumentClasses",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricingComponents_Currency",
                schema: "Core",
                table: "ModelPricingComponents",
                column: "Currency");

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricingComponents_ModelPricingId",
                schema: "Core",
                table: "ModelPricingComponents",
                column: "ModelPricingId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricingComponents_ModelPricingId_ComponentType",
                schema: "Core",
                table: "ModelPricingComponents",
                columns: new[] { "ModelPricingId", "ComponentType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricings_Family_IsActive",
                schema: "Core",
                table: "ModelPricings",
                columns: new[] { "Family", "IsActive" },
                filter: "[Family] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricings_IsActive",
                schema: "Core",
                table: "ModelPricings",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricings_ModelAlias_IsActive",
                schema: "Core",
                table: "ModelPricings",
                columns: new[] { "ModelAlias", "IsActive" },
                filter: "[ModelAlias] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricings_Provider_ModelId_EffectiveFrom",
                schema: "Core",
                table: "ModelPricings",
                columns: new[] { "Provider", "ModelId", "EffectiveFrom" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelPricings_Provider_ModelId_IsActive",
                schema: "Core",
                table: "ModelPricings",
                columns: new[] { "Provider", "ModelId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingSignatureOverrides_OrganizationId_WorkflowTypeId",
                schema: "Core",
                table: "PendingSignatureOverrides",
                columns: new[] { "OrganizationId", "WorkflowTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingSignatureOverrides_OrganizationId_WorkflowTypeId_PlaceholderName",
                schema: "Core",
                table: "PendingSignatureOverrides",
                columns: new[] { "OrganizationId", "WorkflowTypeId", "PlaceholderName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RagSettings_BlockId",
                schema: "Core",
                table: "RagSettings",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_SharedComponentId",
                schema: "Core",
                table: "Sections",
                column: "SharedComponentId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_WorkflowId",
                schema: "Core",
                table: "Sections",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedComponentVersions_ContentHash",
                schema: "Core",
                table: "SharedComponentVersions",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_SharedComponentVersions_ParentVersionId",
                schema: "Core",
                table: "SharedComponentVersions",
                column: "ParentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SharedComponentVersions_SharedComponentId_SequenceNumber",
                schema: "Core",
                table: "SharedComponentVersions",
                columns: new[] { "SharedComponentId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureFieldOverrideAuditLogs_OrganizationId_WorkflowTypeId_CreatedAt",
                schema: "Core",
                table: "SignatureFieldOverrideAuditLogs",
                columns: new[] { "OrganizationId", "WorkflowTypeId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureFieldOverrides_OrganizationId_WorkflowTypeId_PlaceholderName",
                schema: "Core",
                table: "SignatureFieldOverrides",
                columns: new[] { "OrganizationId", "WorkflowTypeId", "PlaceholderName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SignatureOverrideVotes_PendingOverrideId_VoterUserId",
                schema: "Core",
                table: "SignatureOverrideVotes",
                columns: new[] { "PendingOverrideId", "VoterUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TemplateCategories_DisplayOrder",
                schema: "Core",
                table: "TemplateCategories",
                column: "DisplayOrder");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateCategories_Name",
                schema: "Core",
                table: "TemplateCategories",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_TemplateCategories_ParentCategoryId",
                schema: "Core",
                table: "TemplateCategories",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TextInputs_BlockId",
                schema: "Core",
                table: "TextInputs",
                column: "BlockId");

            migrationBuilder.CreateIndex(
                name: "IX_ValidationSchemas_OrganizationId_SourceInstance_SourceSchemaId",
                schema: "Core",
                table: "ValidationSchemas",
                columns: new[] { "OrganizationId", "SourceInstance", "SourceSchemaId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowRegressionField_WorkflowId",
                schema: "Core",
                table: "WorkflowRegressionField",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_Workflows_WorkflowTypeId",
                schema: "Core",
                table: "Workflows",
                column: "WorkflowTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowSignatureField_WorkflowId",
                schema: "Core",
                table: "WorkflowSignatureField",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTypeOrganizationAccess_OrganizationId",
                schema: "Core",
                table: "WorkflowTypeOrganizationAccess",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTypeOrganizationAccess_WorkflowTypeId_OrganizationId",
                schema: "Core",
                table: "WorkflowTypeOrganizationAccess",
                columns: new[] { "WorkflowTypeId", "OrganizationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTypes_CategoryId",
                schema: "Core",
                table: "WorkflowTypes",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTypes_DuplicatedFromWorkflowTypeId",
                schema: "Core",
                table: "WorkflowTypes",
                column: "DuplicatedFromWorkflowTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowVariable_WorkflowId",
                schema: "Core",
                table: "WorkflowVariable",
                column: "WorkflowId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowVersions_ContentHash",
                schema: "Core",
                table: "WorkflowVersions",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowVersions_ParentVersionId",
                schema: "Core",
                table: "WorkflowVersions",
                column: "ParentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowVersions_WorkflowTypeId_CreatedAt",
                schema: "Core",
                table: "WorkflowVersions",
                columns: new[] { "WorkflowTypeId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlockTemplate",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "DocumentClasses",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "ExcelAnalysisPrompts",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "GraphRagPrompts",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "ModelPricingComponents",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "PdfOcrPrompts",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "RagSettings",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "ReconciliationPrompts",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "ScreeningPrompts",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "SharedComponentCategories",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "SharedComponents",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "SharedComponentVersions",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "SignatureFieldOverrideAuditLogs",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "SignatureFieldOverrides",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "SignatureOverrideVotes",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "TextInputs",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "TopicsPrompts",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "UseAutoDetectedTableOfContentsPrompts",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "ValidationPrompts",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "ValidationSchemas",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "WorkflowRegressionField",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "WorkflowSignatureField",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "WorkflowTypeCounterpartyTypeMap",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "WorkflowTypeOrganizationAccess",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "WorkflowVariable",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "WorkflowVersions",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "DocumentCategories",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "TemplateCategories",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "ModelPricings",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "PendingSignatureOverrides",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "Blocks",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "Sections",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "Workflows",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "WorkflowTypes",
                schema: "Core");

            migrationBuilder.DropTable(
                name: "WorkflowTypeCategories",
                schema: "Core");
        }
    }
}

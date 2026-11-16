using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Business.Reporting;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.DailyAllowances;
using IniaAi.Domain.Entities.DataManagement;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.Screening;
using IniaAi.Domain.Entities.SelfServe;
using IniaAi.Domain.Entities.Testing;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.Tools;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Validation;
using IniaAi.Domain.Entities.Verification;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.ExecutionStates.Abstractions;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Properties;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Graph;
using IniaAi.Infrastructure.Persistence.Extensions;
using IniaAi.Infrastructure.Persistence.Serialization;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using IniaAi.Infrastructure.Persistence.Serialization.Citations;
using IniaAi.Infrastructure.Persistence.Serialization.Counterparties;
using IniaAi.Infrastructure.Persistence.UserData.EntityConfigurations;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OpenIddict.EntityFrameworkCore.Models;
using System.Data.Common;
using System.Text.Json;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Infrastructure.Persistence.UserData.Contexts;

public class UserDataDbContext(DbContextOptions<UserDataDbContext> options)
    : IdentityDbContext<User>(options), IDataProtectionKeyContext, IUserDataDbContext
{
    public const string SCHEMA_NAME = "UserData";

    public DbSet<Counterparty> Counterparties { get; set; } = default!;
    public DbSet<CounterpartyType> CounterpartyTypes { get; set; } = default!;
    public DbSet<DocumentClass> DocumentClasses { get; set; } = default!;
    public DbSet<HedgeFundsMonitoringObservation> HedgeFundsMonitoringObservations { get; set; } = default!;
    public DbSet<HedgeFundsMonitoringObservationAudit> HedgeFundsMonitoringObservationAudits { get; set; } = default!;
    public DbSet<HedgeFundsControlResult> HedgeFundsControlResults { get; set; } = default!;
    public DbSet<PrivateEquityMonitoringObservation> PrivateEquityMonitoringObservations { get; set; } = default!;
    public DbSet<PrivateEquityMonitoringAllocation> PrivateEquityMonitoringAllocations { get; set; } = default!;
    public DbSet<PrivateEquityMonitoringObservationAudit> PrivateEquityMonitoringObservationAudits { get; set; } = default!;
    public DbSet<RegressionTestResult> RegressionTestResults { get; set; } = default!;
    public DbSet<WorkflowRegressionRun> WorkflowRegressionRuns { get; set; } = default!;
    public DbSet<WorkflowRegressionBlockResult> WorkflowRegressionBlockResults { get; set; } = default!;
    public DbSet<WorkflowBaselineRegistration> WorkflowBaselineRegistrations { get; set; } = default!;
    public DbSet<ValidationCase> ValidationCases { get; set; } = default!;
    public DbSet<ValidationRun> ValidationRuns { get; set; } = default!;
    public DbSet<ValidationSuite> ValidationSuites { get; set; } = default!;
    public DbSet<ValidationSchedule> ValidationSchedules { get; set; } = default!;
    public DbSet<ValidationThresholdSettings> ValidationThresholdSettings { get; set; } = default!;
    public DbSet<ValidationReferenceAuditLog> ValidationReferenceAuditLogs { get; set; } = default!;
    public DbSet<Document> Documents { get; set; } = default!;
    public DbSet<DocumentUnlockAuditLog> DocumentUnlockAuditLogs { get; set; } = default!;
    public DbSet<ScreeningRecord> ScreeningRecords { get; set; } = default!;
    public DbSet<ScreeningBacktest> ScreeningBacktests { get; set; } = default!;
    public DbSet<ScreeningBacktestDocument> ScreeningBacktestDocuments { get; set; } = default!;
    public DbSet<ScreeningBacktestRun> ScreeningBacktestRuns { get; set; } = default!;
    public DbSet<DocumentInfo> DocumentInfos { get; set; } = default!;
    public DbSet<EmbeddingVector> EmbeddingVectors { get; set; } = default!;
    public DbSet<File> Files { get; set; } = default!; // For blob storage only
    public DbSet<FileMetadata> FileMetadata { get; set; } = default!; // For file metadata
    public DbSet<Json2XlsxTemplate> Json2XlsxTemplates { get; set; } = default!;
    public DbSet<Json2XlsxTemplateVersion> Json2XlsxTemplateVersions { get; set; } = default!;
    public DbSet<ConversionType> ConversionTypes { get; set; } = default!;
    public DbSet<Organization> Organizations { get; set; } = default!;
    public DbSet<UserOrganization> UserOrganizations { get; set; } = default!;
    public DbSet<UserWorkflowTypeVisibility> UserWorkflowTypeVisibilities { get; set; } = default!;
    public DbSet<Page> Pages { get; set; } = default!;
    public DbSet<AgentRunTraceFile> AgentRunTraceFiles { get; set; } = default!;
    public DbSet<AgentRunOutput> AgentRunOutputs { get; set; } = default!;
    public DbSet<AgentRunSessionBundle> AgentRunSessionBundles { get; set; } = default!;
    public DbSet<AgentConversation> AgentConversations { get; set; } = default!;
    public DbSet<AgentConversationTurn> AgentConversationTurns { get; set; } = default!;
    public DbSet<EditorAgentDraft> EditorAgentDrafts { get; set; } = default!;
    public DbSet<EditorAgentProposal> EditorAgentProposals { get; set; } = default!;
    public DbSet<EditorAgentTurnReports> EditorAgentTurnReports { get; set; } = default!;
    public DbSet<EditorAgentTurnFeedback> EditorAgentTurnFeedback { get; set; } = default!;
    public DbSet<EditorAgentChangeDecision> EditorAgentChangeDecisions { get; set; } = default!;
    public DbSet<EditorAgentSpendUnlock> EditorAgentSpendUnlocks { get; set; } = default!;
    public DbSet<BlockCitationTrace> BlockCitationTraces { get; set; } = default!;
    public DbSet<VerificationCriterionRecord> VerificationCriterionRecords { get; set; } = default!;
    public DbSet<Block> Blocks { get; set; } = default!;
    public DbSet<BlockResponse> BlockResponses { get; set; } = default!;
    public DbSet<Workflow> Workflows { get; set; } = default!;
    public DbSet<Section> Sections { get; set; } = default!;
    public DbSet<RagSettings> RagSettings { get; set; } = default!;
    public DbSet<TextInput> TextInputs { get; set; } = default!;
    public DbSet<WorkflowReport> WorkflowReports { get; set; } = default!;
    public DbSet<ReportSection> ReportSections { get; set; } = default!;
    public DbSet<ReportParagraph> ReportParagraphs { get; set; } = default!;
    public DbSet<ReportDataOutput> ReportDataOutputs { get; set; } = default!;
    public DbSet<ReportFeedbackItem> ReportFeedbackItems { get; set; } = default!;
    public DbSet<WorkflowExecutionCounterparty> WorkflowExecutionCounterparties { get; set; } = default!;
    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; } = default!;
    public DbSet<WorkflowExecution> WorkflowExecutions { get; set; } = default!;
    public DbSet<WorkflowExecutionLog> WorkflowExecutionLogs { get; set; } = default!;
    public DbSet<HostBoot> HostBoots { get; set; } = default!;

    // Graph entities for Graph-RAG
    public DbSet<GraphNode> GraphNodes { get; set; } = default!;
    public DbSet<GraphEdge> GraphEdges { get; set; } = default!;

    // Token usage and cost tracking
    public DbSet<TokenUsage> TokenUsages { get; set; } = default!;
    public DbSet<TokenUsageComponent> TokenUsageComponents { get; set; } = default!;
    public DbSet<TokenUsageDailyTotal> TokenUsageDailyTotals { get; set; } = default!;
    public DbSet<TokenUsageExecutionTotal> TokenUsageExecutionTotals { get; set; } = default!;
    public DbSet<OrganizationModelSettings> OrganizationModelSettings { get; set; } = default!;
    public DbSet<OrganizationCleanupSettings> OrganizationCleanupSettings { get; set; } = default!;

    // Content refinement presets
    public DbSet<RefinementPreset> RefinementPresets { get; set; } = default!;

    // Two-Factor Authentication entities
    public DbSet<UserTrustedDevice> UserTrustedDevices { get; set; } = default!;

    public DbSet<CreditAccount> CreditAccounts { get; set; } = default!;
    public DbSet<CreditPack> CreditPacks { get; set; } = default!;
    public DbSet<CreditEntry> CreditEntries { get; set; } = default!;
    public DbSet<Subscription> Subscriptions { get; set; } = default!;
    public DbSet<PaymentEvent> PaymentEvents { get; set; } = default!;
    public DbSet<Consent> Consents { get; set; } = default!;
    public DbSet<HelpRequest> HelpRequests { get; set; } = default!;
    public DbSet<HelpRequestReply> HelpRequestReplies { get; set; } = default!;
    public DbSet<SupportGrant> SupportGrants { get; set; } = default!;
    public DbSet<SupportAccess> SupportAccesses { get; set; } = default!;
    public DbSet<EmailCodeRequest> EmailCodeRequests { get; set; } = default!;
    public DbSet<EmailCodeLockout> EmailCodeLockouts { get; set; } = default!;
    public DbSet<UserSignInMethod> UserSignInMethods { get; set; } = default!;
    public DbSet<DailyAllowanceUse> DailyAllowanceUses { get; set; } = default!;

    // These are needed by OpenIddict
    public DbSet<OpenIddictEntityFrameworkCoreApplication> Applications => Set<OpenIddictEntityFrameworkCoreApplication>();
    public DbSet<OpenIddictEntityFrameworkCoreAuthorization> Authorizations => Set<OpenIddictEntityFrameworkCoreAuthorization>();
    public DbSet<OpenIddictEntityFrameworkCoreScope> Scopes => Set<OpenIddictEntityFrameworkCoreScope>();
    public DbSet<OpenIddictEntityFrameworkCoreToken> Tokens => Set<OpenIddictEntityFrameworkCoreToken>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        return Database.BeginTransactionAsync(cancellationToken);
    }

    public virtual IExecutionStrategy CreateExecutionStrategy()
    {
        return Database.CreateExecutionStrategy();
    }

    public void ClearChangeTracker()
    {
        ChangeTracker.Clear();
    }

    public Dictionary<string, bool> GetTableStatuses()
    {
        string providerName = Database.ProviderName?.ToLower()
            ?? throw new InvalidOperationException("Database provider name is not set");

        bool isPostgres = providerName.Contains("npgsql");
        bool isMySql = providerName.Contains("mysql");
        bool isOracle = providerName.Contains("oracle");
        bool isSqlServer = !isPostgres && !isMySql && !isOracle;

        string[] tableNames =
        [
            "AspNetUsers",
            "AspNetRoles",
            "AspNetUserRoles",
            nameof(Counterparties),
            nameof(CounterpartyTypes),
            nameof(DocumentClasses),
            nameof(HedgeFundsMonitoringObservations),
            nameof(Documents),
            nameof(DocumentInfos),
            nameof(EmbeddingVectors),
            nameof(Files),
            nameof(Organizations),
            nameof(Pages),
            nameof(Blocks),
            nameof(BlockResponses),
            nameof(Workflows),
            nameof(RagSettings),
            nameof(TextInputs),
            nameof(WorkflowExecutions),
            nameof(WorkflowExecutionLogs),
        ];

        string GetQuotedName(string name)
        {
            if (isPostgres)
            {
                return $"\"{name}\"";
            }

            if (isMySql)
            {
                return $"`{name}`";
            }

            if (isOracle)
            {
                return $"\"{name.ToUpper()}\"";
            }

            return $"[{name}]"; // SQL Server
        }

        string GetSchemaTableName(string tableName)
        {
            string schemaName = GetQuotedName(SCHEMA_NAME);
            string quotedTableName = GetQuotedName(tableName);
            return isOracle ? $"{quotedTableName}" : $"{schemaName}.{quotedTableName}";
        }

        string GetExistsClause(string tableName)
        {
            string existsQuery = $"SELECT 1 FROM {GetSchemaTableName(tableName)}";
            return isOracle ? $"EXISTS ({existsQuery})" : $"EXISTS (SELECT 1 FROM {GetSchemaTableName(tableName)})";
        }

        string query = string.Join(" UNION ALL ", tableNames.Select(tableName =>
            $"SELECT '{tableName}' AS TableName, CASE WHEN {GetExistsClause(tableName)} THEN 1 ELSE 0 END AS HasRecords"));

        if (isOracle)
        {
            query = $"SELECT * FROM ({query})";
        }

        using DbCommand command = Database.GetDbConnection().CreateCommand();  // Raw SQL reviewed: table-existence probe; table and schema names from model metadata, quoted; covers the CommandText line below
        command.CommandText = query;
        Database.OpenConnection();

        using DbDataReader result = command.ExecuteReader();
        var tableStatuses = new Dictionary<string, bool>();
        while (result.Read())
        {
            tableStatuses[result.GetString(0)] = Convert.ToBoolean(result.GetInt32(1));
        }

        return tableStatuses;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        _ = modelBuilder.Ignore<WorkflowType>();
        modelBuilder.MapDocumentClassPageOverviews();
        _ = modelBuilder.Ignore<WorkflowTypeOrganizationAccess>();
        _ = modelBuilder.Ignore<WorkflowTypeCounterpartyTypeMap>();
        _ = modelBuilder.Ignore<TableOutput>();
        _ = modelBuilder.Ignore<TableConfig>();
        _ = modelBuilder.Ignore<WorkflowTypeCategory>();
        _ = modelBuilder.Ignore<BlockProperties>();
        _ = modelBuilder.Ignore<DocumentCategory>();

        // ModelPricing is a Core entity, ignore it and its child entity in UserData context
        // to prevent EF from discovering it through TokenUsage.ModelPricing navigation
        _ = modelBuilder.Ignore<ModelPricing>();
        _ = modelBuilder.Ignore<ModelPricingComponent>();

        _ = modelBuilder.Ignore<List<string>>();
        _ = modelBuilder.Ignore<Dictionary<string, string?>>();
        _ = modelBuilder.Ignore<Dictionary<string, string>>();

        modelBuilder.ApplySoftDeleteFilter();

        _ = modelBuilder.HasDefaultSchema(SCHEMA_NAME);

        // The one Fluent API exception for custody rows: data annotations cannot express a CHECK
        // constraint, and a bundle naming both a block and a conversation, or neither, is a row
        // no reaper owns. The custodian refuses such an anchor; the database refuses the row.
        _ = modelBuilder.Entity<AgentRunSessionBundle>().ToTable(table => table.HasCheckConstraint(
            AgentRunSessionBundleOwnerCheck.NAME,
            AgentRunSessionBundleOwnerCheck.Sql(Database.IsSqlServer())));

        // The same exception for the credit ledger: the code keeps a pack's counts within what it
        // granted and an account's pending credits and debt at zero or more, and the database refuses
        // a row that breaks those bounds whatever wrote it.
        bool isSqlServer = Database.IsSqlServer();
        _ = modelBuilder.Entity<CreditPack>().ToTable(table =>
        {
            _ = table.HasCheckConstraint("CK_CreditPacks_RemainingWithinAmount", CreditLedgerBoundsChecks.WithinAmount(nameof(CreditPack.Remaining), isSqlServer));
            _ = table.HasCheckConstraint("CK_CreditPacks_ReversedWithinAmount", CreditLedgerBoundsChecks.WithinAmount(nameof(CreditPack.Reversed), isSqlServer));
            _ = table.HasCheckConstraint("CK_CreditPacks_DebtRepaidWithinAmount", CreditLedgerBoundsChecks.WithinAmount(nameof(CreditPack.DebtRepaid), isSqlServer));
        });
        _ = modelBuilder.Entity<CreditAccount>().ToTable(table =>
        {
            _ = table.HasCheckConstraint("CK_CreditAccounts_PendingNotNegative", CreditLedgerBoundsChecks.NotNegative(nameof(CreditAccount.Pending), isSqlServer));
            _ = table.HasCheckConstraint("CK_CreditAccounts_DebtNotNegative", CreditLedgerBoundsChecks.NotNegative(nameof(CreditAccount.Debt), isSqlServer));
        });

        _ = modelBuilder.Entity<ReportSection>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            PropertyBuilder<GridLayoutOutput?> layoutProperty = entity.Property(e => e.Layout)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, WorkflowOutputDataJsonDefaults.Options),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : JsonSerializer.Deserialize<GridLayoutOutput>(v, WorkflowOutputDataJsonDefaults.Options));

            layoutProperty.Metadata.SetValueComparer(new ValueComparer<GridLayoutOutput?>(
                (c1, c2) => (c1 == null && c2 == null)
                    || (c1 != null && c2 != null
                        && JsonSerializer.Serialize(c1, WorkflowOutputDataJsonDefaults.Options)
                            == JsonSerializer.Serialize(c2, WorkflowOutputDataJsonDefaults.Options)),
                c => c == null
                    ? 0
                    : JsonSerializer.Serialize(c, WorkflowOutputDataJsonDefaults.Options).GetHashCode(),
                c => c == null
                    ? null
                    : JsonSerializer.Deserialize<GridLayoutOutput>(
                        JsonSerializer.Serialize(c, WorkflowOutputDataJsonDefaults.Options),
                        WorkflowOutputDataJsonDefaults.Options)));

            _ = layoutProperty.HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");
        });

        _ = modelBuilder.Entity<ReportParagraph>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            PropertyBuilder<ChartOutput?> chartPayloadProperty = entity.Property(e => e.ChartPayload)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, WorkflowOutputDataJsonDefaults.Options),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : JsonSerializer.Deserialize<ChartOutput>(v, WorkflowOutputDataJsonDefaults.Options));

            chartPayloadProperty.Metadata.SetValueComparer(new ValueComparer<ChartOutput?>(
                (c1, c2) => (c1 == null && c2 == null)
                    || (c1 != null && c2 != null
                        && JsonSerializer.Serialize(c1, WorkflowOutputDataJsonDefaults.Options)
                            == JsonSerializer.Serialize(c2, WorkflowOutputDataJsonDefaults.Options)),
                c => c == null
                    ? 0
                    : JsonSerializer.Serialize(c, WorkflowOutputDataJsonDefaults.Options).GetHashCode(),
                c => c == null
                    ? null
                    : JsonSerializer.Deserialize<ChartOutput>(
                        JsonSerializer.Serialize(c, WorkflowOutputDataJsonDefaults.Options),
                        WorkflowOutputDataJsonDefaults.Options)));

            _ = chartPayloadProperty.HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            PropertyBuilder<TableOutput?> tablePayloadProperty = entity.Property(e => e.TablePayload)
                .HasConversion(
                    v => v == null ? null : JsonSerializer.Serialize(v, WorkflowOutputDataJsonDefaults.Options),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : JsonSerializer.Deserialize<TableOutput>(v, WorkflowOutputDataJsonDefaults.Options));

            tablePayloadProperty.Metadata.SetValueComparer(new ValueComparer<TableOutput?>(
                (c1, c2) => (c1 == null && c2 == null)
                    || (c1 != null && c2 != null
                        && JsonSerializer.Serialize(c1, WorkflowOutputDataJsonDefaults.Options)
                            == JsonSerializer.Serialize(c2, WorkflowOutputDataJsonDefaults.Options)),
                c => c == null
                    ? 0
                    : JsonSerializer.Serialize(c, WorkflowOutputDataJsonDefaults.Options).GetHashCode(),
                c => c == null
                    ? null
                    : JsonSerializer.Deserialize<TableOutput>(
                        JsonSerializer.Serialize(c, WorkflowOutputDataJsonDefaults.Options),
                        WorkflowOutputDataJsonDefaults.Options)));

            _ = tablePayloadProperty.HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            if (Database.IsNpgsql())
            {
                _ = entity.Property(e => e.Sources).HasColumnType("text[]");
            }
            else if (Database.IsSqlServer())
            {
                var jsonConverter = new ValueConverter<List<string>, string>(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());

                _ = entity.Property(e => e.Sources)
                    .HasConversion(jsonConverter)
                    .HasColumnType("nvarchar(max)")
                    .HasDefaultValueSql("N'[]'");
            }

            _ = ConfigureCitationsJsonColumn(entity.Property(e => e.Citations), providerName);

            // Rows written before this column existed need a valid empty array: an empty string,
            // the default EF would pick for them, is not valid jsonb.
            _ = ConfigureCitationsJsonColumn(entity.Property(e => e.CitedSourcesByBlock), providerName)
                .HasDefaultValueSql(providerName?.Contains("npgsql") == true ? "'[]'::jsonb" : "N'[]'");

            _ = entity.HasMany(e => e.WorkflowExecutionLogs)
                .WithOne()
                .HasForeignKey("ReportParagraphId")
                .OnDelete(DeleteBehavior.SetNull);
        });

        _ = modelBuilder.Entity<ReportDataOutput>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            _ = ConfigureCitationsJsonColumn(entity.Property(e => e.Citations), providerName);
            _ = ConfigureCitationsJsonColumn(entity.Property(e => e.CitedSourcesByBlock), providerName);
        });

        _ = modelBuilder.Entity<Block>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            _ = entity.Property(e => e.ExecutionState)
                .HasConversion(
                    v => v == null ? null : v.ToJson(false),
                    v => string.IsNullOrEmpty(v) ? null : BlockExecutionState.FromJson(v))
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            // Add value comparer to ensure proper change tracking
            entity.Property(e => e.ExecutionState)
                .Metadata.SetValueComparer(new ValueComparer<BlockExecutionState?>(
                    (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.ToJson(false) == c2.ToJson(false)),
                    c => c == null ? 0 : c.ToJson(false).GetHashCode(),
                    c => c == null ? null : BlockExecutionState.FromJson(c.ToJson(false))));

            _ = entity.Property(b => b.Properties)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, BlockPropertiesJsonDefaults.Options),
                    v => JsonSerializer.Deserialize<BlockProperties>(v, BlockPropertiesJsonDefaults.Options)!)
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            // Without a value comparer EF Core falls back to reference equality for this
            // JSON-converted reference-typed column, which means in-place mutations to
            // Block.Properties.ExecutionState (performed by block strategies during
            // workflow execution) are invisible to the change tracker and never persisted.
            entity.Property(e => e.Properties)
                .Metadata.SetValueComparer(new ValueComparer<BlockProperties>(
                    (c1, c2) => ReferenceEquals(c1, c2)
                        || (c1 != null
                            && c2 != null
                            && JsonSerializer.Serialize(c1, BlockPropertiesJsonDefaults.Options)
                                == JsonSerializer.Serialize(c2, BlockPropertiesJsonDefaults.Options)),
                    c => c == null
                        ? 0
                        : JsonSerializer.Serialize(c, BlockPropertiesJsonDefaults.Options).GetHashCode(),
                    c => JsonSerializer.Deserialize<BlockProperties>(
                        JsonSerializer.Serialize(c, BlockPropertiesJsonDefaults.Options),
                        BlockPropertiesJsonDefaults.Options)!));

            SharedComponentBindingsColumn.Apply(entity.Property(b => b.SharedComponentBindings), providerName);

            _ = entity.HasOne(b => b.Section)
                .WithMany(s => s.Blocks)
                .HasForeignKey(b => b.SectionId)
                .OnDelete(DeleteBehavior.ClientCascade);
        });

        _ = modelBuilder.Entity<RagSettings>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            _ = entity.Property(e => e.ExecutionState)
                .HasConversion(
                    v => v == null ? null : v.ToJson(false),
                    v => string.IsNullOrEmpty(v) ? null : RagSettingsExecutionState.FromJson(v))
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            // Add value comparer to ensure proper change tracking
            entity.Property(e => e.ExecutionState)
                .Metadata.SetValueComparer(new ValueComparer<RagSettingsExecutionState?>(
                    (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.ToJson(false) == c2.ToJson(false)),
                    c => c == null ? 0 : c.ToJson(false).GetHashCode(),
                    c => c == null ? null : RagSettingsExecutionState.FromJson(c.ToJson(false))));
        });

        _ = modelBuilder.Entity<TextInput>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            _ = entity.Property(e => e.ExecutionState)
                .HasConversion(
                    v => v == null ? null : v.ToJson(false),
                    v => string.IsNullOrEmpty(v) ? null : TextInputExecutionState.FromJson(v))
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            // Add value comparer to ensure proper change tracking
            entity.Property(e => e.ExecutionState)
                .Metadata.SetValueComparer(new ValueComparer<TextInputExecutionState?>(
                    (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.ToJson(false) == c2.ToJson(false)),
                    c => c == null ? 0 : c.ToJson(false).GetHashCode(),
                    c => c == null ? null : TextInputExecutionState.FromJson(c.ToJson(false))));
        });

        _ = modelBuilder.Entity<Counterparty>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            CounterpartyPropertiesColumn.Apply(entity.Property(b => b.Properties), providerName);
        });

        _ = modelBuilder.Entity<WorkflowExecutionCounterparty>()
            .HasKey(e => new { e.WorkflowExecutionId, e.CounterpartyId });

        _ = modelBuilder.Entity<WorkflowExecutionCounterparty>()
            .HasOne(wc => wc.WorkflowExecution)
            .WithMany(w => w.WorkflowExecutionsCounterparties)
            .HasForeignKey(wc => wc.WorkflowExecutionId);

        _ = modelBuilder.Entity<WorkflowExecutionCounterparty>()
            .HasOne(wc => wc.Counterparty)
            .WithMany(c => c.WorkflowExecutionCounterparties)
            .HasForeignKey(wc => wc.CounterpartyId);

        _ = modelBuilder.Entity<Workflow>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            _ = entity.Property(e => e.ExecutionState)
                .HasConversion(
                    v => v == null ? null : v.ToJson(false),
                    v => string.IsNullOrEmpty(v) ? null : WorkflowExecutionState.FromJson(v))
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            entity.Property(e => e.ExecutionState)
                .Metadata.SetValueComparer(new ValueComparer<WorkflowExecutionState?>(
                    (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.ToJson(false) == c2.ToJson(false)),
                    c => c == null ? 0 : c.ToJson(false).GetHashCode(),
                    c => c == null ? null : WorkflowExecutionState.FromJson(c.ToJson(false))));

            // Configure the Configuration property for user workflows
            _ = entity.Property(x => x.Configuration)
                .HasConversion(
                    v => v == null
                        ? null
                        : JsonSerializer.Serialize(v, JsonDefaults.Options),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : JsonSerializer.Deserialize<WorkflowConfiguration>(v, JsonDefaults.Options))
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            // Add ValueComparer for proper change tracking of Configuration
            entity.Property(x => x.Configuration)
                .Metadata.SetValueComparer(new ValueComparer<WorkflowConfiguration?>(
                    (c1, c2) => (c1 == null && c2 == null) ||
                                (c1 != null && c2 != null &&
                                JsonSerializer.Serialize(c1, JsonDefaults.Options) ==
                                JsonSerializer.Serialize(c2, JsonDefaults.Options)),
                    c => c == null ? 0 : JsonSerializer.Serialize(c, JsonDefaults.Options).GetHashCode(),
                    c => c == null ? null : JsonSerializer.Deserialize<WorkflowConfiguration>(
                        JsonSerializer.Serialize(c, JsonDefaults.Options), JsonDefaults.Options)));
        });

        _ = modelBuilder.Entity<Section>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            _ = entity.Property(e => e.ExecutionState)
                .HasConversion(
                    v => v == null ? null : v.ToJson(false),
                    v => string.IsNullOrEmpty(v) ? null : WorkflowExecutionState.FromJson(v))
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            entity.Property(e => e.ExecutionState)
                .Metadata.SetValueComparer(new ValueComparer<WorkflowExecutionState?>(
                    (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.ToJson(false) == c2.ToJson(false)),
                    c => c == null ? 0 : c.ToJson(false).GetHashCode(),
                    c => c == null ? null : WorkflowExecutionState.FromJson(c.ToJson(false))));

            _ = entity.Property(x => x.Properties)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonDefaults.Options),
                    v => string.IsNullOrEmpty(v)
                        ? new SectionProperties()
                        : JsonSerializer.Deserialize<SectionProperties>(v, JsonDefaults.Options) ?? new SectionProperties())
                .IsRequired()
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            entity.Property(x => x.Properties)
                .Metadata.SetValueComparer(new ValueComparer<SectionProperties>(
                    (c1, c2) => JsonSerializer.Serialize(c1, JsonDefaults.Options) ==
                                JsonSerializer.Serialize(c2, JsonDefaults.Options),
                    c => JsonSerializer.Serialize(c, JsonDefaults.Options).GetHashCode(),
                    c => JsonSerializer.Deserialize<SectionProperties>(
                        JsonSerializer.Serialize(c, JsonDefaults.Options), JsonDefaults.Options) ?? new SectionProperties()));

            _ = entity.Property(x => x.Configuration)
                .HasConversion(
                    v => v == null
                        ? null
                        : JsonSerializer.Serialize(v, JsonDefaults.Options),
                    v => string.IsNullOrEmpty(v)
                        ? null
                        : JsonSerializer.Deserialize<SectionConfiguration>(v, JsonDefaults.Options))
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            entity.Property(x => x.Configuration)
                .Metadata.SetValueComparer(new ValueComparer<SectionConfiguration?>(
                    (c1, c2) => (c1 == null && c2 == null) ||
                                (c1 != null && c2 != null &&
                                JsonSerializer.Serialize(c1, JsonDefaults.Options) ==
                                JsonSerializer.Serialize(c2, JsonDefaults.Options)),
                    c => c == null ? 0 : JsonSerializer.Serialize(c, JsonDefaults.Options).GetHashCode(),
                    c => c == null ? null : JsonSerializer.Deserialize<SectionConfiguration>(
                        JsonSerializer.Serialize(c, JsonDefaults.Options), JsonDefaults.Options)));

            SharedComponentBindingsColumn.Apply(entity.Property(x => x.SharedComponentBindings), providerName);
            SharedComponentSlotStatesColumn.Apply(entity.Property(x => x.SharedComponentSlotStates), providerName);
        });

        _ = modelBuilder.Entity<WorkflowExecution>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            _ = entity.Property(e => e.Variables)
                .HasConversion(
                    v => v == null
                        ? null
                        : JsonSerializer.Serialize(v, JsonDefaults.Options),
                    v => string.IsNullOrEmpty(v)
                        ? new Dictionary<string, object>()
                        : JsonSerializer.Deserialize<Dictionary<string, object>>(v, JsonDefaults.Options)!)
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            entity.Property(e => e.Variables)
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, object>>(
                    (c1, c2) => JsonSerializer.Serialize(c1, JsonDefaults.Options) ==
                                JsonSerializer.Serialize(c2, JsonDefaults.Options),
                    c => JsonSerializer.Serialize(c, JsonDefaults.Options).GetHashCode(),
                    c => JsonSerializer.Deserialize<Dictionary<string, object>>(
                        JsonSerializer.Serialize(c, JsonDefaults.Options), JsonDefaults.Options)!));
        });

        _ = modelBuilder.Entity<WorkflowExecutionLog>(entity =>
        {
            _ = entity.HasOne(log => log.WorkflowExecution)
                .WithMany(exe => exe.Logs)
                .HasForeignKey(log => log.WorkflowExecutionId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            _ = entity.Property(x => x.WorkflowId)
               .HasMaxLength(450);

            _ = entity.Property(x => x.BlockId)
               .HasMaxLength(450);
        });

        _ = modelBuilder.Entity<WorkflowExecutionLog>(entity =>
        {
            PropertyBuilder<Dictionary<string, string?>> property = entity.Property(e => e.Inputs);

            var comparer = new ValueComparer<Dictionary<string, string?>>(
                 (d1, d2) =>
                    (d1 == null && d2 == null) ||
                    (d1 != null && d2 != null &&
                    JsonSerializer.Serialize(d1, JsonDefaults.Options) ==
                    JsonSerializer.Serialize(d2, JsonDefaults.Options)),

                 d => d == null
                    ? 0
                    : JsonSerializer.Serialize(d, JsonDefaults.Options).GetHashCode(),

                 d => JsonSerializer.Deserialize<Dictionary<string, string?>>(
                        JsonSerializer.Serialize(d ?? new Dictionary<string, string?>(), JsonDefaults.Options),
                        JsonDefaults.Options)
                    ?? new Dictionary<string, string?>()
             );

            property.Metadata.SetValueComparer(comparer);

            string providerName = Database?.ProviderName?.ToLowerInvariant() ?? string.Empty;

            _ = property
                .HasConversion(
                    v => JsonSerializer.Serialize(v, JsonDefaults.Options),
                    v => JsonSerializer.Deserialize<Dictionary<string, string?>>(v, JsonDefaults.Options)
                        ?? new Dictionary<string, string?>()
                )
                .HasColumnType(providerName.Contains("npgsql") ? "jsonb" : "nvarchar(max)");
        });

        _ = modelBuilder.Entity<DocumentInfo>(b =>
        {
            _ = b.Property(x => x.JobId)
                .HasMaxLength(100);
            _ = b.HasIndex(e => e.JobId);
            _ = b.HasOne(di => di.Document)
                .WithMany()
                .HasForeignKey(di => di.DocumentId)
                .IsRequired(false);
        });

        _ = modelBuilder.Entity<Document>(b =>
        {
            _ = b.Property(x => x.WorkflowTypeId)
                .HasMaxLength(100);
            _ = b.HasIndex(e => e.WorkflowTypeId);
        });

        _ = modelBuilder.Entity<Page>()
            .HasOne(p => p.Document)
            .WithMany(d => d.Pages)
            .HasForeignKey(p => p.DocumentId)
            .IsRequired(false);

        _ = modelBuilder.Entity<WorkflowVariable>(entity =>
        {
            _ = entity.HasOne(v => v.Workflow)
                .WithMany(w => w.Variables)
                .HasForeignKey(v => v.WorkflowId)
                .IsRequired(false);

            string? providerName = Database.ProviderName?.ToLower();

            _ = entity.Property(e => e.Options)
                .HasConversion(
                    v => v == null
                        ? null
                        : JsonSerializer.Serialize(v, JsonDefaults.OptionsWithOriginalKeys),
                    v => string.IsNullOrEmpty(v)
                        ? new Dictionary<string, string>()
                        : JsonSerializer.Deserialize<Dictionary<string, string>>(v, JsonDefaults.OptionsWithOriginalKeys)!)
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            entity.Property(e => e.Options)
                .Metadata.SetValueComparer(new ValueComparer<Dictionary<string, string>>(
                    (c1, c2) => JsonSerializer.Serialize(c1, JsonDefaults.OptionsWithOriginalKeys) ==
                        JsonSerializer.Serialize(c2, JsonDefaults.OptionsWithOriginalKeys),
                    c => JsonSerializer.Serialize(c, JsonDefaults.OptionsWithOriginalKeys).GetHashCode(),
                    c => JsonSerializer.Deserialize<Dictionary<string, string>>(
                        JsonSerializer.Serialize(c, JsonDefaults.OptionsWithOriginalKeys), JsonDefaults.OptionsWithOriginalKeys)!));
        });

        _ = modelBuilder.Entity<WorkflowExecutionCounterparty>()
            .HasOne(wc => wc.WorkflowExecution)
            .WithMany(we => we.WorkflowExecutionsCounterparties)
            .HasForeignKey(wc => wc.WorkflowExecutionId)
            .IsRequired(false);

        _ = modelBuilder.Entity<HedgeFundsMonitoringObservationAudit>()
            .HasOne(a => a.HedgeFundsMonitoringObservation)
            .WithMany()
            .HasForeignKey(a => a.HedgeFundsMonitoringObservationId)
            .IsRequired(true)
            .OnDelete(DeleteBehavior.NoAction);

        _ = modelBuilder.Entity<PrivateEquityMonitoringObservationAudit>()
            .HasOne(a => a.PrivateEquityMonitoringObservation)
            .WithMany()
            .HasForeignKey(a => a.PrivateEquityMonitoringObservationId)
            .IsRequired(true)
            .OnDelete(DeleteBehavior.NoAction);

        _ = modelBuilder.ApplyConfiguration(new PageConfiguration());
        _ = modelBuilder.ApplyConfiguration(new FileConfiguration());
        _ = modelBuilder.ApplyConfiguration(new FileMetadataConfiguration());
        _ = modelBuilder.ApplyConfiguration(new Json2XlsxTemplateConfiguration());
        _ = modelBuilder.ApplyConfiguration(new ConversionTypeConfiguration());
        _ = modelBuilder.ApplyConfiguration(new UserConfiguration());
        _ = modelBuilder.ApplyConfiguration(new UserOrganizationConfiguration(this));

        // Graph-RAG entity configurations
        _ = modelBuilder.ApplyConfiguration(new GraphNodeConfiguration());
        _ = modelBuilder.ApplyConfiguration(new GraphEdgeConfiguration());

        // Token usage and cost tracking configurations
        _ = modelBuilder.ApplyConfiguration(new TokenUsageConfiguration(Database));
        _ = modelBuilder.ApplyConfiguration(new TokenUsageComponentConfiguration(Database));
        _ = modelBuilder.ApplyConfiguration(new OrganizationModelSettingsConfiguration(Database));

        // Workflow regression testing configurations
        _ = modelBuilder.ApplyConfiguration(new WorkflowRegressionRunConfiguration(Database));
        _ = modelBuilder.ApplyConfiguration(new WorkflowRegressionBlockResultConfiguration(Database));

        // At most one Active baseline registration per (org, type, counterparty). Filtered unique index
        // cannot be expressed via data annotations, so it is configured here.
        _ = modelBuilder.Entity<WorkflowBaselineRegistration>(entity =>
        {
            string activeFilter = Database.IsNpgsql()
                ? "\"State\" = 0 AND \"IsDeleted\" = false"
                : "[State] = 0 AND [IsDeleted] = 0";

            _ = entity.HasIndex(r => new { r.OrganizationId, r.WorkflowTypeId, r.CounterpartyId })
                .IsUnique()
                .HasFilter(activeFilter);
        });

        _ = modelBuilder.Entity<ValidationRun>(entity =>
        {
            _ = entity.HasIndex(r => new { r.CreatedAt, r.Id })
                .HasDatabaseName(ValidationRunAwaitingTrimIndex.NAME)
                .IsDescending(true, false)
                .HasFilter(ValidationRunAwaitingTrimIndex.Filter(Database.IsNpgsql()));
        });

        // Two-Factor Authentication entity configurations
        _ = modelBuilder.Entity<UserTrustedDevice>(entity =>
        {
            _ = entity.HasKey(e => e.Id);
            _ = entity.Property(e => e.Id).HasMaxLength(450);
            _ = entity.Property(e => e.UserId).IsRequired().HasMaxLength(450);
            _ = entity.Property(e => e.DeviceFingerprint).IsRequired().HasMaxLength(256);
            _ = entity.Property(e => e.DeviceName).HasMaxLength(256);
            _ = entity.Property(e => e.IpAddress).HasMaxLength(45);
            _ = entity.Property(e => e.UserAgent).HasMaxLength(512);

            _ = entity.HasOne(e => e.User)
                .WithMany()
                .HasForeignKey(e => e.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            _ = entity.HasIndex(e => new { e.UserId, e.DeviceFingerprint });
            _ = entity.HasIndex(e => e.TrustedUntil);
        });
    }

    private static PropertyBuilder<List<T>> ConfigureCitationsJsonColumn<T>(PropertyBuilder<List<T>> property, string? providerName)
    {
        _ = property.HasConversion(
            v => JsonSerializer.Serialize(v, CitationsJsonDefaults.Options),
            v => string.IsNullOrEmpty(v)
                ? new List<T>()
                : JsonSerializer.Deserialize<List<T>>(v, CitationsJsonDefaults.Options)
                    ?? new List<T>());

        property.Metadata.SetValueComparer(new ValueComparer<List<T>>(
            (c1, c2) => c1 != null
                && c2 != null
                && JsonSerializer.Serialize(c1, CitationsJsonDefaults.Options)
                    == JsonSerializer.Serialize(c2, CitationsJsonDefaults.Options),
            c => c == null
                ? 0
                : JsonSerializer.Serialize(c, CitationsJsonDefaults.Options).GetHashCode(),
            c => c == null
                ? new List<T>()
                : JsonSerializer.Deserialize<List<T>>(
                    JsonSerializer.Serialize(c, CitationsJsonDefaults.Options),
                    CitationsJsonDefaults.Options)
                    ?? new List<T>()));

        return property.HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");
    }
}

/// <summary>
/// Quotes a column for a check constraint's SQL. SQL Server quotes identifiers with brackets;
/// PostgreSQL and SQLite with double quotes, which PostgreSQL needs because the columns are mixed case.
/// </summary>
internal static class CheckConstraintSql
{
    public static string Quote(string column, bool isSqlServer)
    {
        return isSqlServer ? $"[{column}]" : $"\"{column}\"";
    }
}

/// <summary>
/// The credit ledger's bounds as SQL.
/// </summary>
internal static class CreditLedgerBoundsChecks
{
    public static string WithinAmount(string column, bool isSqlServer)
    {
        string quoted = CheckConstraintSql.Quote(column, isSqlServer);
        return $"{quoted} >= 0 AND {quoted} <= {CheckConstraintSql.Quote(nameof(CreditPack.Amount), isSqlServer)}";
    }

    public static string NotNegative(string column, bool isSqlServer)
    {
        return $"{CheckConstraintSql.Quote(column, isSqlServer)} >= 0";
    }
}

/// <summary>
/// The exactly-one-owner rule of <see cref="AgentRunSessionBundle"/> as SQL.
/// </summary>
internal static class AgentRunSessionBundleOwnerCheck
{
    public const string NAME = "CK_AgentRunSessionBundles_ExactlyOneOwner";

    public static string Sql(bool isSqlServer)
    {
        string block = CheckConstraintSql.Quote(nameof(AgentRunSessionBundle.BlockId), isSqlServer);
        string conversation = CheckConstraintSql.Quote(nameof(AgentRunSessionBundle.ConversationId), isSqlServer);

        return $"({block} IS NOT NULL AND {conversation} IS NULL) OR ({block} IS NULL AND {conversation} IS NOT NULL)";
    }
}

using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.DocumentAnalysis;
using IniaAi.Domain.Entities.Screening;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.StoredKeys;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Validation;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Tools.GraphRag;
using IniaAi.Domain.Tools.PdfOcr;
using IniaAi.Domain.Tools.RagStrategies;
using IniaAi.Domain.Tools.Reconciliation;
using IniaAi.Domain.Tools.Validation;
using IniaAi.Infrastructure.Persistence.Core.EntityConfigurations;
using IniaAi.Infrastructure.Persistence.Core.Guards;
using IniaAi.Infrastructure.Persistence.Extensions;
using IniaAi.Infrastructure.Persistence.Serialization;
using IniaAi.Infrastructure.Persistence.Serialization.Blocks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data.Common;
using File = IniaAi.Domain.Entities.Documents.File;

namespace IniaAi.Infrastructure.Persistence.Core.Contexts;

public class CoreDbContext : DbContext, ICoreDbContext
{
    public const string SCHEMA_NAME = "Core";

    public DbSet<Block> Blocks { get; set; } = default!;
    public DbSet<TextInput> TextInputs { get; set; } = default!;
    public DbSet<RagSettings> RagSettings { get; set; } = default!;
    public DbSet<Workflow> Workflows { get; set; } = default!;
    public DbSet<WorkflowVersion> WorkflowVersions { get; set; } = default!;
    public DbSet<Section> Sections { get; set; } = default!;
    public DbSet<DocumentClass> DocumentClasses { get; set; } = default!;
    public DbSet<UseAutoDetectedTableOfContentsPrompts> UseAutoDetectedTableOfContentsPrompts { get; set; } = default!;
    public DbSet<PdfOcrPrompts> PdfOcrPrompts { get; set; } = default!;
    public DbSet<GraphRagPrompts> GraphRagPrompts { get; set; } = default!;
    public DbSet<ReconciliationPrompts> ReconciliationPrompts { get; set; } = default!;
    public DbSet<WorkflowType> WorkflowTypes { get; set; }
    public DbSet<WorkflowTypeCategory> WorkflowTypeCategories { get; set; } = default!;
    public DbSet<WorkflowTypeOrganizationAccess> WorkflowTypeOrganizationAccess { get; set; } = default!;
    public DbSet<BlockTemplate> BlockTemplates { get; set; } = default!;
    public DbSet<TemplateCategory> TemplateCategories { get; set; } = default!;
    public DbSet<DocumentCategory> DocumentCategories { get; set; } = default!;
    public DbSet<ModelPricing> ModelPricings { get; set; } = default!;
    public DbSet<ModelPricingComponent> ModelPricingComponents { get; set; } = default!;
    public DbSet<PendingSignatureOverride> PendingSignatureOverrides { get; set; } = default!;
    public DbSet<SignatureOverrideVote> SignatureOverrideVotes { get; set; } = default!;
    public DbSet<SignatureFieldOverride> SignatureFieldOverrides { get; set; } = default!;
    public DbSet<SignatureFieldOverrideAuditLog> SignatureFieldOverrideAuditLogs { get; set; } = default!;
    public DbSet<OrganizationStoredKey> OrganizationStoredKeys { get; set; } = default!;
    public DbSet<SharedComponent> SharedComponents { get; set; } = default!;
    public DbSet<SharedComponentVersion> SharedComponentVersions { get; set; } = default!;
    public DbSet<SharedComponentCategory> SharedComponentCategories { get; set; } = default!;
    public DbSet<ValidationSchema> ValidationSchemas { get; set; } = default!;
    public DbSet<ValidationPrompts> ValidationPrompts { get; set; } = default!;
    public DbSet<ScreeningPrompts> ScreeningPrompts { get; set; } = default!;
    public DbSet<ExcelAnalysisPrompts> ExcelAnalysisPrompts { get; set; } = default!;
    public DbSet<TopicsPrompts> TopicsPrompts { get; set; } = default!;

    public CoreDbContext(DbContextOptions<CoreDbContext> options) : base(options)
    {
    }

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

    /// <summary>
    /// Every save refuses deletion of shared-component registry rows (see
    /// <see cref="SharedComponentDeletionGuard"/>): retiring is the only end-of-life transition.
    /// Overriding the bool overloads covers all four SaveChanges entry points. The guard's
    /// ChangeTracker.Entries() call already ran change detection, so auto-detection is suppressed
    /// for the base call to avoid scanning the full tracked graph twice per save.
    /// </summary>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        SharedComponentDeletionGuard.Validate(ChangeTracker);
        bool autoDetectChanges = ChangeTracker.AutoDetectChangesEnabled;
        ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }
        finally
        {
            ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
    }

    /// <inheritdoc cref="SaveChanges(bool)"/>
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        SharedComponentDeletionGuard.Validate(ChangeTracker);
        bool autoDetectChanges = ChangeTracker.AutoDetectChangesEnabled;
        ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
        finally
        {
            ChangeTracker.AutoDetectChangesEnabled = autoDetectChanges;
        }
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
            nameof(Workflows),
            nameof(Blocks),
            nameof(TextInputs),
            nameof(RagSettings),
            nameof(UseAutoDetectedTableOfContentsPrompts),
            nameof(PdfOcrPrompts),
            nameof(GraphRagPrompts),
            nameof(ReconciliationPrompts),
            nameof(ValidationPrompts),
            nameof(ScreeningPrompts),
            nameof(ExcelAnalysisPrompts),
            nameof(TopicsPrompts),
            nameof(WorkflowTypes),
            nameof(WorkflowTypeCategories),
            nameof(WorkflowTypeOrganizationAccess),
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

        _ = modelBuilder.Ignore<List<string>>();
        _ = modelBuilder.Ignore<BlockProperties>();
        modelBuilder.MapDocumentClassPageOverviews();

        modelBuilder.ApplySoftDeleteFilter();

        _ = modelBuilder.HasDefaultSchema(SCHEMA_NAME);

        _ = modelBuilder.Ignore<User>();
        _ = modelBuilder.Ignore<Counterparty>();
        _ = modelBuilder.Ignore<CounterpartyType>();
        _ = modelBuilder.Ignore<Document>();
        _ = modelBuilder.Ignore<DocumentInfo>();
        _ = modelBuilder.Ignore<EmbeddingVector>();
        _ = modelBuilder.Ignore<File>();
        _ = modelBuilder.Ignore<Organization>();
        _ = modelBuilder.Ignore<Page>();
        _ = modelBuilder.Ignore<BlockResponse>();
        _ = modelBuilder.Ignore<AgentRunTraceFile>();
        _ = modelBuilder.Ignore<AgentRunOutput>();
        _ = modelBuilder.Ignore<AgentRunSessionBundle>();
        _ = modelBuilder.Ignore<BlockCitationTrace>();
        _ = modelBuilder.Ignore<WorkflowReport>();
        _ = modelBuilder.Ignore<ReportSection>();
        _ = modelBuilder.Ignore<ReportParagraph>();
        _ = modelBuilder.Ignore<ReportDataOutput>();
        _ = modelBuilder.Ignore<WorkflowExecutionCounterparty>();
        _ = modelBuilder.Ignore<WorkflowExecution>();
        _ = modelBuilder.Ignore<WorkflowExecutionLog>();
        _ = modelBuilder.Ignore<FileMetadata>();

        _ = modelBuilder.Entity<Block>()
            .Ignore(q => q.Documents)
            .Ignore(q => q.BlockResponse)
            .Ignore(q => q.BlockResponseId)
            .Ignore(q => q.AgentRunTraceFiles)
            .Ignore(q => q.CitationTrace);

        _ = modelBuilder.ApplyConfiguration(new WorkflowTypeOrganizationAccessConfiguration());
        _ = modelBuilder.ApplyConfiguration(new BlockTemplateConfiguration(Database));
        _ = modelBuilder.ApplyConfiguration(new TemplateCategoryConfiguration());
        _ = modelBuilder.ApplyConfiguration(new DocumentCategoryConfiguration());
        _ = modelBuilder.ApplyConfiguration(new EntityConfigurations.WorkflowConfiguration(Database));
        _ = modelBuilder.ApplyConfiguration(new EntityConfigurations.SectionJsonColumnConfiguration(Database));

        _ = modelBuilder.ApplyConfiguration(new WorkflowTypeConfiguration());
        _ = modelBuilder.ApplyConfiguration(new WorkflowTypeCounterpartyTypeMapConfiguration());
        _ = modelBuilder.ApplyConfiguration(new ModelPricingConfiguration(Database));
        _ = modelBuilder.ApplyConfiguration(new ModelPricingComponentConfiguration(Database));

        _ = modelBuilder.Entity<Block>(entity =>
        {
            string? providerName = Database.ProviderName?.ToLower();

            _ = entity.Property(b => b.Properties)
                .HasConversion(
                    v => System.Text.Json.JsonSerializer.Serialize(v, BlockPropertiesJsonDefaults.CoreOptions),
                    v => System.Text.Json.JsonSerializer.Deserialize<BlockProperties>(v, BlockPropertiesJsonDefaults.CoreOptions)!
                )
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            _ = entity.Property(b => b.RegressionSchema)
                .HasColumnType(providerName?.Contains("npgsql") == true ? "jsonb" : "nvarchar(max)");

            SharedComponentBindingsColumn.Apply(entity.Property(b => b.SharedComponentBindings), providerName);

            _ = entity.HasIndex(b => b.SharedComponentId);

            _ = entity.HasOne(b => b.Section)
                .WithMany(s => s.Blocks)
                .HasForeignKey(b => b.SectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

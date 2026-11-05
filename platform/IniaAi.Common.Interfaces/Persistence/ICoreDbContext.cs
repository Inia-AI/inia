using IniaAi.Application.Common.Interfaces.Contexts;
using IniaAi.Domain.Entities.DocumentAnalysis;
using IniaAi.Domain.Entities.Screening;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.StoredKeys;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Tools.PdfOcr;
using IniaAi.Domain.Tools.RagStrategies;
using IniaAi.Domain.Tools.Reconciliation;
using IniaAi.Domain.Tools.Validation;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Common.Interfaces.Persistence;

/// <summary>
/// Defines the contract for the Core database context,
/// which manages tools, workflow definitions, and prompt sets.
/// </summary>
public interface ICoreDbContext : IBaseDbContext
{
    /// <summary>
    /// The Blocks entity set.
    /// </summary>
    DbSet<Block> Blocks { get; set; }

    /// <summary>
    /// The TextInputs entity set.
    /// </summary>
    DbSet<TextInput> TextInputs { get; set; }

    /// <summary>
    /// The RagSettings entity set.
    /// </summary>
    DbSet<RagSettings> RagSettings { get; set; }

    /// <summary>
    /// The Workflows entity set.
    /// </summary>
    DbSet<Workflow> Workflows { get; set; }

    /// <summary>
    /// The Workflow Versions entity set - append-only template snapshot history.
    /// </summary>
    DbSet<WorkflowVersion> WorkflowVersions { get; set; }

    /// <summary>
    /// The Auto Detected Table of Contents Prompts entity set.
    /// </summary>
    DbSet<UseAutoDetectedTableOfContentsPrompts> UseAutoDetectedTableOfContentsPrompts { get; set; }

    /// <summary>
    /// The PDF OCR Prompts entity set.
    /// </summary>
    DbSet<PdfOcrPrompts> PdfOcrPrompts { get; set; }

    /// <summary>
    /// The Reconciliation Prompts entity set.
    /// </summary>
    DbSet<ReconciliationPrompts> ReconciliationPrompts { get; set; }

    /// <summary>
    /// The Document Intake Screening prompts entity set.
    /// </summary>
    DbSet<ScreeningPrompts> ScreeningPrompts { get; set; }

    /// <summary>
    /// The validation engine's prompts entity set.
    /// </summary>
    DbSet<ValidationPrompts> ValidationPrompts { get; set; }

    /// <summary>
    /// The Excel analysis prompts entity set.
    /// </summary>
    DbSet<ExcelAnalysisPrompts> ExcelAnalysisPrompts { get; set; }

    /// <summary>
    /// The Build Topics prompts entity set.
    /// </summary>
    DbSet<TopicsPrompts> TopicsPrompts { get; set; }

    /// <summary>
    /// The Workflow Types entity set.
    /// </summary>
    DbSet<WorkflowType> WorkflowTypes { get; set; }

    /// <summary>
    /// The Workflow Type Categories entity set.
    /// </summary>
    DbSet<WorkflowTypeCategory> WorkflowTypeCategories { get; set; }

    /// <summary>
    /// The Model Pricing entity set - global pricing data for AI models.
    /// </summary>
    DbSet<ModelPricing> ModelPricings { get; set; }

    /// <summary>
    /// The Model Pricing Components entity set - normalized pricing components (Input, Output, Cache, etc.).
    /// </summary>
    DbSet<ModelPricingComponent> ModelPricingComponents { get; set; }

    /// <summary>
    /// The Pending Signature Overrides entity set - client-proposed overrides awaiting reviewer votes.
    /// </summary>
    DbSet<PendingSignatureOverride> PendingSignatureOverrides { get; set; }

    /// <summary>
    /// The Signature Override Votes entity set - approve/reject votes on pending overrides.
    /// </summary>
    DbSet<SignatureOverrideVote> SignatureOverrideVotes { get; set; }

    /// <summary>
    /// The Signature Field Overrides entity set - active client-authored override values
    /// keyed by <c>(OrganizationId, BlockId, PlaceholderName)</c>. Deliberately a separate
    /// table so client values don't ride along in <c>.workflow</c> JSON exports.
    /// </summary>
    DbSet<SignatureFieldOverride> SignatureFieldOverrides { get; set; }

    /// <summary>
    /// The Signature Field Override Audit Log entity set - append-only history of every
    /// value transition (Created/Updated/Removed) on <see cref="SignatureFieldOverride"/>.
    /// </summary>
    DbSet<SignatureFieldOverrideAuditLog> SignatureFieldOverrideAuditLogs { get; set; }

    /// <summary>
    /// The Organization Stored Keys entity set - credentials an organization stores for its
    /// workflows, encrypted, so the workflow holds only their names.
    /// </summary>
    DbSet<OrganizationStoredKey> OrganizationStoredKeys { get; set; }

    /// <summary>
    /// Returns a dictionary with the names of key tables and a boolean indicating whether they contain records.
    /// </summary>
    /// <returns>
    /// A dictionary mapping table names to a boolean indicating if they have any records.
    /// </returns>
    Dictionary<string, bool> GetTableStatuses();

}

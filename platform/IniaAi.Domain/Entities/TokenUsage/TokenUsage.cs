using IniaAi.Domain.Common;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// Unified table for capturing all token usage across the platform.
/// Enables granular cost tracking and analytics by organization, user, workflow, block, etc.
/// </summary>
public class TokenUsage : Entity<TokenUsage>
{
    // ═══════════════════════════════════════════════════════════════
    // DIMENSIONAL KEYS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// The organization this usage belongs to (required for multi-tenant isolation).
    /// </summary>
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>
    /// The user who triggered this usage (nullable for system operations).
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// The workflow type/template (nullable for non-workflow operations).
    /// </summary>
    public string? WorkflowTypeId { get; set; }

    /// <summary>
    /// The specific workflow execution/report instance (nullable for tests/document processing).
    /// </summary>
    public string? WorkflowExecutionId { get; set; }

    /// <summary>
    /// The workflow section (nullable).
    /// </summary>
    public string? SectionId { get; set; }

    /// <summary>
    /// The specific block that consumed tokens (nullable for non-block operations).
    /// </summary>
    public string? BlockId { get; set; }

    /// <summary>
    /// The document being processed (nullable for non-document operations).
    /// </summary>
    public string? DocumentId { get; set; }

    /// <summary>
    /// The specific page being processed (nullable).
    /// </summary>
    public string? PageId { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // TYPE CLASSIFICATIONS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// What triggered the token usage (LLM execution, embedding, OCR, etc.).
    /// </summary>
    public UsageType UsageType { get; set; }

    /// <summary>
    /// How the operation was triggered (workflow, test, document upload, etc.).
    /// </summary>
    public Enums.ExecutionContext ExecutionContext { get; set; }

    /// <summary>
    /// The type of block that consumed tokens (nullable for non-block operations).
    /// </summary>
    public BlockTypes? BlockType { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // PROVIDER & MODEL
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// The gateway/API endpoint used (e.g., "aws-bedrock", "anthropic-api").
    /// </summary>
    public string Provider { get; set; } = string.Empty;

    /// <summary>
    /// The model vendor/family (e.g., "anthropic").
    /// </summary>
    public string Family { get; set; } = string.Empty;

    /// <summary>
    /// The specific model used (e.g., "claude-sonnet-4-5").
    /// </summary>
    public string ModelId { get; set; } = string.Empty;

    // ═══════════════════════════════════════════════════════════════
    // TIMING
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Duration of the operation (for performance tracking).
    /// </summary>
    public TimeSpan? Duration { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // DESCRIPTION & METADATA
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Human-readable description of what this token usage was for.
    /// E.g., "AI Query block 'Market Analysis' in Document Review workflow"
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// JSON metadata for additional context (e.g., RAG selected page IDs, strategy info).
    /// </summary>
    public string? Metadata { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // PRICING REFERENCE
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Reference to the ModelPricing record used to calculate costs.
    /// </summary>
    public string? ModelPricingId { get; set; }

    /// <summary>
    /// Whether the usage-totals sweep has added this row to <see cref="TokenUsageDailyTotal"/> and
    /// <see cref="TokenUsageExecutionTotal"/>. A usage read sums the totals plus the rows not yet in them.
    /// </summary>
    public bool IsInTotals { get; set; }

    // ═══════════════════════════════════════════════════════════════
    // NAVIGATION PROPERTIES
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// The organization this usage belongs to.
    /// </summary>
    public virtual Organization? Organization { get; set; }

    /// <summary>
    /// The user who triggered this usage.
    /// </summary>
    public virtual User? User { get; set; }

    /// <summary>
    /// The workflow execution this usage is part of.
    /// </summary>
    public virtual WorkflowExecution? WorkflowExecution { get; set; }

    /// <summary>
    /// The workflow type/template.
    /// </summary>
    public virtual WorkflowType? WorkflowType { get; set; }

    /// <summary>
    /// The document being processed.
    /// </summary>
    public virtual Document? Document { get; set; }

    /// <summary>
    /// The specific block that consumed tokens.
    /// </summary>
    public virtual Block? Block { get; set; }

    /// <summary>
    /// The pricing record used to calculate costs.
    /// </summary>
    public virtual ModelPricing? ModelPricing { get; set; }

    /// <summary>
    /// The usage components for this record (normalized structure).
    /// Each component represents a different type of usage (Input, Output, CacheRead, etc.)
    /// and mirrors the ModelPricingComponent structure for easy cost calculation.
    /// </summary>
    public virtual ICollection<TokenUsageComponent> Components { get; set; } = [];
}

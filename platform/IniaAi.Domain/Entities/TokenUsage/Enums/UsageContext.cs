using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.TokenUsage.Enums;

/// <summary>
/// Specifies the context in which a model is used.
/// Used for per-organization model configuration.
/// </summary>
public enum UsageContext
{
    // ═══════════════════════════════════════════════════════════════
    // LLM CONTEXTS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// AI Query blocks and workflow reasoning.
    /// </summary>
    [EnumMember(Value = "BlockExecution")]
    BlockExecution = 0,

    /// <summary>
    /// Document analysis and processing.
    /// </summary>
    [EnumMember(Value = "DocumentProcessing")]
    DocumentProcessing = 1,

    /// <summary>
    /// Financial analysis workflows.
    /// </summary>
    [EnumMember(Value = "FinancialAnalysis")]
    FinancialAnalysis = 3,

    // ═══════════════════════════════════════════════════════════════
    // EMBEDDING CONTEXTS
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Default RAG embeddings.
    /// </summary>
    [EnumMember(Value = "Embedding")]
    Embedding = 100,

    /// <summary>
    /// Financial document embeddings.
    /// </summary>
    [EnumMember(Value = "EmbeddingFinancial")]
    EmbeddingFinancial = 101
}

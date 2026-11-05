using System.Runtime.Serialization;

namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Defines the context in which a model is used.
/// </summary>
public enum UsageContext
{
    // LLM Contexts
    /// <summary>
    /// AI Query block execution.
    /// </summary>
    [EnumMember(Value = "BlockExecution")]
    BlockExecution = 0,

    /// <summary>
    /// Document processing and analysis.
    /// </summary>
    [EnumMember(Value = "DocumentProcessing")]
    DocumentProcessing = 1,

    /// <summary>
    /// Financial analysis workflows.
    /// </summary>
    [EnumMember(Value = "FinancialAnalysis")]
    FinancialAnalysis = 3,

    // Embedding Contexts
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

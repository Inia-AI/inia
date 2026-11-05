using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.TokenUsage.Enums;

/// <summary>
/// Specifies the type of pricing model.
/// </summary>
public enum PricingType
{
    /// <summary>
    /// Large Language Model pricing (input/output/cache tokens).
    /// </summary>
    [EnumMember(Value = "Llm")]
    Llm = 0,

    /// <summary>
    /// Embedding model pricing (per token).
    /// </summary>
    [EnumMember(Value = "Embedding")]
    Embedding = 1
}

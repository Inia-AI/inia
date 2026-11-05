namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Specifies the type of pricing model.
/// </summary>
public enum PricingType
{
    /// <summary>
    /// Large Language Model pricing (input/output/cache tokens).
    /// </summary>
    Llm = 0,

    /// <summary>
    /// Embedding model pricing (per token).
    /// </summary>
    Embedding = 1
}

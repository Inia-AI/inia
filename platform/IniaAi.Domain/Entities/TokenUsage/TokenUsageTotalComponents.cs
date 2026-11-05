using IniaAi.Domain.Entities.TokenUsage.Enums;
using System.Linq.Expressions;

namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// Which ledger components each token sum of a <see cref="TokenUsageTotal{T}"/> counts. The sweep
/// adds by these and a usage read sums the rows not yet in the totals by the same expressions, so
/// the two cannot disagree. Cost counts every component, whatever its type.
/// </summary>
public static class TokenUsageTotalComponents
{
    public static readonly Expression<Func<TokenUsageComponent, bool>> IsInput =
        c => c.ComponentType == PricingComponentType.Input;

    public static readonly Expression<Func<TokenUsageComponent, bool>> IsOutput =
        c => c.ComponentType == PricingComponentType.Output;

    public static readonly Expression<Func<TokenUsageComponent, bool>> IsCacheWrite =
        c => c.ComponentType == PricingComponentType.CacheWrite5m || c.ComponentType == PricingComponentType.CacheWrite1h;

    public static readonly Expression<Func<TokenUsageComponent, bool>> IsCacheRead =
        c => c.ComponentType == PricingComponentType.CacheRead;

    public static readonly Expression<Func<TokenUsageComponent, bool>> IsEmbedding =
        c => c.ComponentType == PricingComponentType.Embedding;
}

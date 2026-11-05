using IniaAi.Domain.Entities.TokenUsage.Enums;

namespace IniaAi.Common.Interfaces.Services;

/// <summary>
/// Helper class for building UsageComponent collections from token counts.
/// </summary>
public static class UsageComponentHelper
{
    /// <summary>
    /// Builds a list of UsageComponents from individual token counts.
    /// </summary>
    public static List<UsageComponent> FromTokens(
        int inputTokens,
        int outputTokens,
        int cacheCreationTokens = 0,
        int cacheReadTokens = 0)
    {
        var components = new List<UsageComponent>();

        if (inputTokens > 0)
        {
            components.Add(new UsageComponent { ComponentType = PricingComponentType.Input, Units = inputTokens });
        }

        if (outputTokens > 0)
        {
            components.Add(new UsageComponent { ComponentType = PricingComponentType.Output, Units = outputTokens });
        }

        if (cacheCreationTokens > 0)
        {
            components.Add(new UsageComponent { ComponentType = PricingComponentType.CacheWrite5m, Units = cacheCreationTokens });
        }

        if (cacheReadTokens > 0)
        {
            components.Add(new UsageComponent { ComponentType = PricingComponentType.CacheRead, Units = cacheReadTokens });
        }

        return components;
    }

    /// <summary>
    /// Builds a list with a single Embedding component.
    /// </summary>
    public static List<UsageComponent> ForEmbedding(int tokens)
    {
        return tokens <= 0 ? [] : [new UsageComponent { ComponentType = PricingComponentType.Embedding, Units = tokens }];
    }
}

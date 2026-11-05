using IniaAi.Domain.Entities.TokenUsage.Enums;

namespace IniaAi.Domain.Entities.TokenUsage;

/// <summary>
/// Prices one model request from a <see cref="ModelPricing"/> row. A row with a
/// <see cref="ModelPricing.LongPromptThresholdTokens"/> prices a request whose prompt is over the
/// threshold from its <c>LongPrompt*</c> components, for every token of the request, output
/// included; each request is priced on its own.
/// </summary>
public static class ModelPricingRates
{
    private const decimal PER_MILLION = 1_000_000m;
    private const decimal PER_THOUSAND = 1_000m;
    private const string GLOBAL_PROFILE_PREFIX = "global.";

    /// <summary>The token kinds that make up a prompt's length.</summary>
    public static readonly IReadOnlySet<PricingComponentType> PromptComponents = new HashSet<PricingComponentType>
    {
        PricingComponentType.Input,
        PricingComponentType.CacheWrite5m,
        PricingComponentType.CacheWrite1h,
        PricingComponentType.CacheRead,
    };

    /// <summary>True when a request of this prompt length pays the row's long-prompt prices.</summary>
    public static bool IsLongPrompt(ModelPricing? pricing, long promptTokens)
    {
        return pricing?.LongPromptThresholdTokens is int threshold && threshold > 0 && promptTokens > threshold;
    }

    /// <summary>
    /// What <paramref name="units"/> of one component cost. A long-prompt request takes the
    /// component's long-prompt counterpart, or the standard component when the row lacks it.
    /// Zero when the row has no such component.
    /// </summary>
    public static decimal CostOf(ModelPricing? pricing, PricingComponentType componentType, long units, bool longPrompt)
    {
        if (pricing is null || units <= 0)
        {
            return 0m;
        }

        ModelPricingComponent? component = null;
        if (longPrompt && LongPromptCounterpartOf(componentType) is PricingComponentType longType)
        {
            component = pricing.Components.FirstOrDefault(candidate => candidate.ComponentType == longType);
        }

        component ??= pricing.Components.FirstOrDefault(candidate => candidate.ComponentType == componentType);
        if (component is null || component.Cost == 0m)
        {
            return 0m;
        }

        return component.Unit switch
        {
            PricingUnit.PerMillion => units / PER_MILLION * component.Cost,
            PricingUnit.PerThousand => units / PER_THOUSAND * component.Cost,
            PricingUnit.PerUnit => units * component.Cost,
            _ => units / PER_MILLION * component.Cost,
        };
    }

    /// <summary>
    /// What a call costs as a multiple of the row's components, from the endpoint it was sent
    /// to: the row's <see cref="ModelPricing.RegionalEndpointMultiplier"/> for a regional
    /// endpoint, 1 for a <c>global.</c> Bedrock profile or a row with no multiplier. A call whose
    /// endpoint is not known counts as regional: the platform's Bedrock stacks call geo profiles.
    /// </summary>
    public static decimal EndpointMultiplierFor(ModelPricing? pricing, string? endpointModelId)
    {
        if (pricing?.RegionalEndpointMultiplier is not decimal multiplier || multiplier <= 0m)
        {
            return 1m;
        }

        return IsGlobalEndpoint(endpointModelId) ? 1m : multiplier;
    }

    /// <summary>True for a Bedrock global cross-Region inference profile (<c>global.anthropic...</c>).</summary>
    public static bool IsGlobalEndpoint(string? endpointModelId)
    {
        return endpointModelId?.StartsWith(GLOBAL_PROFILE_PREFIX, StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>True for a component that exists only to price long-prompt requests.</summary>
    public static bool IsLongPromptComponent(PricingComponentType componentType)
    {
        return componentType is PricingComponentType.LongPromptInput
            or PricingComponentType.LongPromptOutput
            or PricingComponentType.LongPromptCacheWrite5m
            or PricingComponentType.LongPromptCacheWrite1h
            or PricingComponentType.LongPromptCacheRead;
    }

    /// <summary>The long-prompt component that replaces a standard one, or null when none does.</summary>
    public static PricingComponentType? LongPromptCounterpartOf(PricingComponentType componentType)
    {
        return componentType switch
        {
            PricingComponentType.Input => PricingComponentType.LongPromptInput,
            PricingComponentType.Output => PricingComponentType.LongPromptOutput,
            PricingComponentType.CacheWrite5m => PricingComponentType.LongPromptCacheWrite5m,
            PricingComponentType.CacheWrite1h => PricingComponentType.LongPromptCacheWrite1h,
            PricingComponentType.CacheRead => PricingComponentType.LongPromptCacheRead,
            _ => null,
        };
    }
}

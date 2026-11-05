using IniaAi.AiConnection.Common;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;

namespace IniaAi.Core.Services.TokenUsage;

/// <summary>
/// What an agent run is billed to record. <paramref name="Spend"/> carries the identity every
/// record shares (organization, workflow, block, usage type, description); its components and
/// metadata are replaced.
/// </summary>
/// <param name="Runs">How many runs the bill covers.</param>
/// <param name="RuntimeCostUsd">The runtime's own measure of what the runs spent. It is the bill
/// only for what no pricing row covers: tokens nobody could read, or a model with no row.</param>
/// <param name="Models">The runs' tokens per model and per request, from the trace pack.</param>
/// <param name="ConfiguredModelId">The model the consumer asked the runtime for. Its provider
/// is the provider every model of the run is priced under.</param>
/// <param name="PriceTokens">False when the runtime figure is a part of a bill another record
/// already priced (a turn billed a second time for spend learned late): the tokens are then
/// recorded for counting only, at zero cost.</param>
/// <param name="UncoveredRuntimeCostUsd">The part of <paramref name="RuntimeCostUsd"/> spent by
/// runs whose trace pack was not collected (<see cref="AgentRunTraceTokens.UncoveredCostUsd"/>):
/// no token record covers it, so it is billed as the runtime reported it.</param>
public sealed record AgentRunBill(
    LlmUsageRecord Spend,
    int Runs,
    decimal RuntimeCostUsd,
    IReadOnlyList<AgentRunModelTokens> Models,
    string ConfiguredModelId,
    int Round,
    bool PriceTokens = true,
    decimal UncoveredRuntimeCostUsd = 0m);

/// <summary>
/// Prices an agent run from its tokens. The runtime's cost figure comes from the model SDK's own
/// price table, which has been wrong for a model it did not know (Claude Haiku 5.5 billed at
/// Claude Opus 5.5 rates); the platform's pricing table is the price list it bills
/// everything else from, so the run is billed from that, request by request, so a long-prompt
/// price applies to the requests that pay it.
/// <para>
/// Records written: one per model the run called, carrying that model's tokens and their cost,
/// and one spend record (<see cref="PricingComponentType.AgentRunSpend"/>, units = runs) that
/// carries only the cost no pricing row covered: a model with no row, or a run whose trace pack was
/// not collected. Every reader that sums component costs (the run
/// cost view, the usage dashboard, the editor agent's daily spend) therefore reads the same bill.
/// </para>
/// </summary>
public static class AgentRunBilling
{
    private const string TOKENS_DESCRIPTION_SUFFIX = " tokens";

    /// <param name="ListCostUsd">The same tokens at the row's list price, before any regional
    /// endpoint multiple: the runtime's own figure is a list price, so the part of it no row
    /// covered is found by taking away list cost, not the regional cost.</param>
    private sealed record PricedModel(LlmUsageRecord Record, decimal CostUsd, decimal ListCostUsd, bool Priced);

    /// <summary>
    /// Writes the bill's records and returns what they cost in total. Writes nothing when the
    /// runtime reports no spend and the trace reports no tokens.
    /// </summary>
    public static async Task<decimal> RecordAsync(
        AgentRunBill bill,
        ITokenUsageService tokenUsage,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LlmUsageRecord> records = await BuildAsync(bill, tokenUsage, cancellationToken);
        decimal total = 0m;
        foreach (LlmUsageRecord record in records)
        {
            _ = await tokenUsage.RecordLlmUsageAsync(record, cancellationToken);
            total += record.Components.Sum(static component => component.CostUsd ?? 0m);
        }

        return total;
    }

    /// <summary>The bill's records, each component priced explicitly.</summary>
    public static async Task<IReadOnlyList<LlmUsageRecord>> BuildAsync(
        AgentRunBill bill,
        ITokenUsageService tokenUsage,
        CancellationToken cancellationToken = default)
    {
        string pricingProvider = ModelIdNormalizer.GetProvider(bill.ConfiguredModelId);
        List<PricedModel> models = [];
        foreach (AgentRunModelTokens tokens in bill.Models)
        {
            PricedModel? priced = await PriceAsync(bill, tokens, pricingProvider, tokenUsage, cancellationToken);
            if (priced is not null)
            {
                models.Add(priced);
            }
        }

        if (bill.RuntimeCostUsd <= 0m && models.Count == 0)
        {
            return [];
        }

        decimal pricedListUsd = models.Sum(static model => model.ListCostUsd);
        bool allPriced = bill.PriceTokens && models.Count > 0 && models.All(static model => model.Priced);
        decimal uncoveredUsd = models.Count == 0
            ? bill.RuntimeCostUsd
            : Math.Clamp(bill.UncoveredRuntimeCostUsd, 0m, bill.RuntimeCostUsd);
        decimal coveredRuntimeUsd = bill.RuntimeCostUsd - uncoveredUsd;
        decimal remainderUsd = uncoveredUsd + (allPriced ? 0m : Math.Max(0m, coveredRuntimeUsd - pricedListUsd));
        string provenance = models.Count > 0 ? AgentTokenProvenance.FROM_TRACE : AgentTokenProvenance.UNKNOWN;
        string costSource = allPriced && uncoveredUsd == 0m
            ? AgentTokenProvenance.COST_FROM_TOKENS
            : AgentTokenProvenance.COST_FROM_RUNTIME;

        LlmUsageRecord spend = bill.Spend with
        {
            Components =
            [
                new UsageComponent
                {
                    ComponentType = PricingComponentType.AgentRunSpend,
                    Units = Math.Max(1, bill.Runs),
                    CostUsd = remainderUsd,
                },
            ],
            Metadata = AgentTokenProvenance.MetadataFor(provenance, bill.Round, costSource, bill.RuntimeCostUsd),
            IsSingleRequest = false,
        };

        return [spend, .. models.Select(static model => model.Record)];
    }

    private static async Task<PricedModel?> PriceAsync(
        AgentRunBill bill,
        AgentRunModelTokens tokens,
        string pricingProvider,
        ITokenUsageService tokenUsage,
        CancellationToken cancellationToken)
    {
        string modelId = ModelIdNormalizer.Normalize(tokens.Model);
        ModelPricing? pricing = bill.PriceTokens
            ? await tokenUsage.FindLlmPricingAsync(pricingProvider, modelId, cancellationToken)
            : null;

        decimal endpointMultiplier = ModelPricingRates.EndpointMultiplierFor(pricing, EndpointOf(tokens.Model, bill.ConfiguredModelId));
        Dictionary<PricingComponentType, (long Units, decimal Cost)> totals = [];
        foreach (AgentRunCallTokens call in tokens.Calls)
        {
            bool longPrompt = !call.Summed && ModelPricingRates.IsLongPrompt(pricing, call.PromptTokens);
            Add(totals, pricing, PricingComponentType.Input, call.InputTokens, longPrompt, endpointMultiplier);
            Add(totals, pricing, PricingComponentType.Output, call.OutputTokens, longPrompt, endpointMultiplier);
            Add(totals, pricing, PricingComponentType.CacheWrite5m, call.CacheWrite5mTokens, longPrompt, endpointMultiplier);
            Add(totals, pricing, PricingComponentType.CacheWrite1h, call.CacheWrite1hTokens, longPrompt, endpointMultiplier);
            Add(totals, pricing, PricingComponentType.CacheRead, call.CacheReadInputTokens, longPrompt, endpointMultiplier);
        }

        if (totals.Count == 0)
        {
            return null;
        }

        List<UsageComponent> components =
        [
            .. totals.Select(static entry => new UsageComponent
            {
                ComponentType = entry.Key,
                Units = (int)Math.Clamp(entry.Value.Units, 0, int.MaxValue),
                CostUsd = entry.Value.Cost,
            }),
        ];

        LlmUsageRecord record = bill.Spend with
        {
            Family = ModelIdNormalizer.GetFamily(tokens.Model),
            ModelId = modelId,
            Components = components,
            Description = bill.Spend.Description + TOKENS_DESCRIPTION_SUFFIX,
            Metadata = AgentTokenProvenance.MetadataFor(AgentTokenProvenance.FROM_TRACE, bill.Round),
            IsSingleRequest = false,
        };

        decimal costUsd = components.Sum(static component => component.CostUsd ?? 0m);

        return new PricedModel(record, costUsd, costUsd / endpointMultiplier, pricing is not null);
    }

    /// <summary>
    /// The endpoint a model of the run was called through: the trace's own id when it names a
    /// Bedrock profile, else the configured model's, whose route the runtime calls every model on.
    /// </summary>
    private static string EndpointOf(string traceModelId, string configuredModelId)
    {
        return ModelIdNormalizer.IsBedrockFormat(traceModelId) ? traceModelId : configuredModelId;
    }

    private static void Add(
        Dictionary<PricingComponentType, (long Units, decimal Cost)> totals,
        ModelPricing? pricing,
        PricingComponentType componentType,
        long units,
        bool longPrompt,
        decimal endpointMultiplier)
    {
        if (units <= 0)
        {
            return;
        }

        (long Units, decimal Cost) running = totals.GetValueOrDefault(componentType);
        totals[componentType] = (
            running.Units + units,
            running.Cost + (ModelPricingRates.CostOf(pricing, componentType, units, longPrompt) * endpointMultiplier));
    }
}

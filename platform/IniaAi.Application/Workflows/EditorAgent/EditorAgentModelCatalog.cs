using IniaAi.AiConnection.Common;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Services.AgentRuntime;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// The models a user may pick for a conversation: the runtime's allowlist as the deployment
/// mirrors it, priced from the active <see cref="ModelPricing"/> row of each model. A model priced
/// under several providers takes the row of the provider its allowlist id runs through (a Bedrock
/// inference-profile id, or a registered Foundry deployment name), else the newest active row of
/// any provider.
/// </summary>
public static class EditorAgentModelCatalog
{
    private const string OPUS = "opus";
    private const string SONNET = "sonnet";
    private const string HAIKU = "haiku";
    private const string OPUS_CAPABILITY = "Deepest reasoning, for hard questions about large workflows. Slowest and most expensive.";
    private const string SONNET_CAPABILITY = "Strong reasoning at moderate cost. The right choice for most questions.";
    private const string HAIKU_CAPABILITY = "Fastest and cheapest, for quick lookups such as where a block is or what it reads.";
    private const string OTHER_CAPABILITY = "A general-purpose model.";
    private const decimal THOUSANDS_PER_MILLION = 1000m;
    private const decimal TOKENS_PER_MILLION = 1_000_000m;

    public const string ADVANCED_TIER = "Advanced";
    public const string STANDARD_TIER = "Standard";
    public const string EXPERT_TIER = "Expert";

    /// <summary>
    /// The model a new conversation starts on: the platform's advanced model, else its basic
    /// model, as the runtime allowlist names them. When neither is allowed, the first allowed
    /// model whose id contains <see cref="EditorAgentSettings.DefaultModelMatch"/>, else the first
    /// allowed model.
    /// </summary>
    public static string DefaultModel(AgentRuntimeSettings runtime, EditorAgentSettings editor, EditorAgentTierModels tiers)
    {
        return AllowedForm(runtime, tiers.Advanced)
            ?? AllowedForm(runtime, tiers.Basic)
            ?? runtime.AllowedModels.FirstOrDefault(model =>
                model.Contains(editor.DefaultModelMatch, StringComparison.OrdinalIgnoreCase))
            ?? runtime.AllowedModels.FirstOrDefault()
            ?? string.Empty;
    }

    public static bool IsAllowed(AgentRuntimeSettings runtime, string modelId)
    {
        return runtime.AllowedModels.Contains(modelId, StringComparer.Ordinal);
    }

    /// <summary>
    /// The runtime allowlist's id for a model the platform calls, or null when the allowlist has
    /// no form of it. The same id first, then any allowlisted id of the same model, so a Bedrock
    /// id with another region prefix, or a public API id, still finds its runtime form.
    /// </summary>
    public static string? AllowedForm(AgentRuntimeSettings runtime, string? modelId)
    {
        return AgentRuntimeModels.RuntimeIdFor(runtime, modelId);
    }

    /// <summary>
    /// The models a user may pick, without prices: <see cref="EditorAgentSettings.ExpertModel"/>
    /// first, then the platform's advanced model (the default) and its basic model, each as the
    /// runtime allowlist names it, then <paramref name="currentModelId"/> when the conversation
    /// is on another allowed model. A model the allowlist lacks is left out; when it lacks both
    /// tiers, every allowed model is offered.
    /// </summary>
    public static IReadOnlyList<EditorAgentOfferedModel> Offered(
        AgentRuntimeSettings runtime,
        EditorAgentSettings editor,
        EditorAgentTierModels tiers,
        string? currentModelId)
    {
        string? advanced = AllowedForm(runtime, tiers.Advanced);
        string? basic = AllowedForm(runtime, tiers.Basic);
        if (advanced is null && basic is null)
        {
            string fallback = DefaultModel(runtime, editor, tiers);

            return [.. runtime.AllowedModels.Select(model =>
                new EditorAgentOfferedModel(model, null, string.Equals(model, fallback, StringComparison.Ordinal)))];
        }

        List<EditorAgentOfferedModel> offered = [];
        if (advanced is not null)
        {
            offered.Add(new EditorAgentOfferedModel(advanced, ADVANCED_TIER, IsDefault: true));
        }

        if (basic is not null && !SameModel(basic, advanced))
        {
            offered.Add(new EditorAgentOfferedModel(basic, STANDARD_TIER, IsDefault: advanced is null));
        }

        string? expert = AllowedForm(runtime, editor.ExpertModel);
        if (expert is not null && offered.All(model => !SameModel(model.ModelId, expert)))
        {
            offered.Insert(0, new EditorAgentOfferedModel(expert, EXPERT_TIER, IsDefault: false));
        }

        if (currentModelId is not null
            && IsAllowed(runtime, currentModelId)
            && offered.All(model => !SameModel(model.ModelId, currentModelId)))
        {
            offered.Add(new EditorAgentOfferedModel(currentModelId, null, IsDefault: false));
        }

        return offered;
    }

    /// <summary>
    /// True when <paramref name="modelId"/> is one of <see cref="Offered"/>'s models, or the same
    /// model under another allowlisted prefix.
    /// </summary>
    public static bool IsOffered(
        AgentRuntimeSettings runtime,
        EditorAgentSettings editor,
        EditorAgentTierModels tiers,
        string? currentModelId,
        string modelId)
    {
        return IsAllowed(runtime, modelId)
            && Offered(runtime, editor, tiers, currentModelId).Any(model => SameModel(model.ModelId, modelId));
    }

    /// <summary><see cref="Offered"/> with each model's name, price and what it is good at.</summary>
    public static async Task<IReadOnlyList<EditorAgentModelOption>> ListAsync(
        ICoreUnitOfWork core,
        AgentRuntimeSettings runtime,
        EditorAgentSettings editor,
        EditorAgentTierModels tiers,
        string? currentModelId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<EditorAgentOfferedModel> offered = Offered(runtime, editor, tiers, currentModelId);
        List<string> normalized = [.. offered.Select(static model => ModelIdNormalizer.Normalize(model.ModelId))];
        List<ModelPricing> pricings = await core.Repository<ModelPricing>()
            .GetAll()
            .AsNoTracking()
            .Include(static pricing => pricing.Components)
            .Where(pricing => pricing.IsActive
                && (normalized.Contains(pricing.ModelId)
                    || (pricing.ModelAlias != null && normalized.Contains(pricing.ModelAlias))))
            .ToListAsync(cancellationToken);

        return [.. offered.Select(model =>
        {
            string canonical = ModelIdNormalizer.Normalize(model.ModelId);
            string modelProvider = ModelIdNormalizer.GetProvider(model.ModelId);
            ModelPricing? pricing = pricings
                .Where(row => row.ModelId == canonical || row.ModelAlias == canonical)
                .OrderByDescending(row => row.Provider == modelProvider)
                .ThenByDescending(static row => row.EffectiveFrom)
                .FirstOrDefault();

            return new EditorAgentModelOption(
                model.ModelId,
                string.IsNullOrWhiteSpace(pricing?.DisplayName) ? canonical : pricing.DisplayName,
                CapabilityOf(canonical),
                PerMillion(pricing, PricingComponentType.Input),
                PerMillion(pricing, PricingComponentType.Output),
                model.IsDefault)
            {
                Tier = model.Tier,
            };
        })];
    }

    private static bool SameModel(string first, string? second)
    {
        return second is not null
            && (string.Equals(first, second, StringComparison.Ordinal)
                || ModelIdNormalizer.Normalize(first) == ModelIdNormalizer.Normalize(second));
    }

    private static string CapabilityOf(string canonicalModelId)
    {
        if (canonicalModelId.Contains(OPUS, StringComparison.OrdinalIgnoreCase))
        {
            return OPUS_CAPABILITY;
        }

        if (canonicalModelId.Contains(SONNET, StringComparison.OrdinalIgnoreCase))
        {
            return SONNET_CAPABILITY;
        }

        if (canonicalModelId.Contains(HAIKU, StringComparison.OrdinalIgnoreCase))
        {
            return HAIKU_CAPABILITY;
        }

        return OTHER_CAPABILITY;
    }

    private static decimal? PerMillion(ModelPricing? pricing, PricingComponentType componentType)
    {
        ModelPricingComponent? component = pricing?.Components.FirstOrDefault(c => c.ComponentType == componentType);

        return component?.Unit switch
        {
            PricingUnit.PerMillion => component.Cost,
            PricingUnit.PerThousand => component.Cost * THOUSANDS_PER_MILLION,
            PricingUnit.PerToken => component.Cost * TOKENS_PER_MILLION,
            _ => null,
        };
    }
}

/// <summary>
/// The platform's two default models for one organization, as the deployment calls them: the
/// advanced and the basic model after its settings and the tenant's override. Null where none
/// resolves.
/// </summary>
public sealed record EditorAgentTierModels(string? Advanced, string? Basic)
{
    public static EditorAgentTierModels None { get; } = new(null, null);
}

/// <param name="Tier"><see cref="EditorAgentModelCatalog.ADVANCED_TIER"/>, <see cref="EditorAgentModelCatalog.STANDARD_TIER"/>,
/// <see cref="EditorAgentModelCatalog.EXPERT_TIER"/>, or null.</param>
public sealed record EditorAgentOfferedModel(string ModelId, string? Tier, bool IsDefault);

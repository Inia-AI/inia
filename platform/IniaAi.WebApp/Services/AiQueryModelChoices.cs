using IniaAi.AiConnection.Common;
using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using AnthropicModel = IniaAi.Configuration.Enums.Anthropic.AiModel;

namespace IniaAi.WebApp.Services;

/// <summary>
/// What the AI Query block's model settings offer: each supported model once, by its plain
/// name, which the running stack resolves to its own id (<see cref="ModelRouteResolver"/>), and
/// each model's own effort facts, so the editor never shows one model's default or limit as
/// another's.
/// </summary>
public static class AiQueryModelChoices
{
    private const string MODEL_DEFAULT_SUFFIX = " (model default)";

    public static IReadOnlyList<AnthropicModel> Selectable { get; } = [..
        Enum.GetValues<AnthropicModel>()
            .Where(static model => model.IsActiveEnumEntry())
            .OrderBy(static model => model.GetVisualOrder())];

    /// <summary>
    /// The name of the model an id stands for, so a block that stores an older stack-specific id
    /// shows as the same model as its plain name. An unknown id shows as itself.
    /// </summary>
    public static string Describe(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId)
            || !Utils.TryParseEnumFromEnumMemberAttribute(modelId, out AnthropicModel _))
        {
            return modelId ?? string.Empty;
        }

        string modelName = ModelIdNormalizer.Normalize(modelId, ModelDeploymentTable.Empty);

        return Utils.TryParseEnumFromEnumMemberAttribute(modelName, out AnthropicModel model)
            ? model.GetDescription()
            : modelId;
    }

    public static string DefaultEffortPlaceholder(AnthropicModel model)
    {
        return $"Model default ({model.GetDefaultEffort().GetDescription()})";
    }

    /// <summary>
    /// Every effort level with its label, the model's own default marked.
    /// </summary>
    public static IReadOnlyList<(ModelEffort Effort, string Label)> EffortOptions(AnthropicModel model)
    {
        ModelEffort defaultEffort = model.GetDefaultEffort();

        return [.. Enum.GetValues<ModelEffort>()
            .Select(effort => (effort, effort == defaultEffort
                ? effort.GetDescription() + MODEL_DEFAULT_SUFFIX
                : effort.GetDescription()))];
    }

    /// <summary>
    /// Why the chosen effort will not reach the model, or null when it will.
    /// </summary>
    public static string? EffortCapNotice(AnthropicModel model, bool shouldUseThinkingMode, ModelEffort? effort)
    {
        ModelEffort? effortSent = model.GetEffortSent(shouldUseThinkingMode, effort);

        return effortSent == effort
            ? null
            : $"Without thinking, this model runs at {effortSent?.GetDescription()} effort at most.";
    }
}

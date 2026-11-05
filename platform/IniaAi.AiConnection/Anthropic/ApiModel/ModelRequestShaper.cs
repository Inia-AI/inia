using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Configuration.Enums.Anthropic;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

internal static class ModelRequestShaper
{
    public static AiModel? ParseModelId(string? modelId)
    {
        return modelId is not null && Utils.TryParseEnumFromEnumMemberAttribute(modelId, out AiModel parsedModel)
            ? parsedModel
            : null;
    }

    /// <summary>
    /// Applies the request shape adaptive-generation models require and returns true,
    /// or returns false leaving the request untouched for every other model. These models
    /// reject non-default <c>temperature</c>/<c>top_p</c>/<c>top_k</c> and budget-based
    /// thinking with a 400; thinking requests use adaptive mode, non-thinking requests
    /// switch thinking off explicitly ("disabled", or "between_tools" for Sonnet 5.5; always-on
    /// models get no thinking setting, since they reject both).
    /// Effort rides along only here because <c>output_config</c> is rejected by models
    /// that predate it (and Haiku 4.5 never supports it). A model that rejects thinking-off
    /// requests above some effort (Opus 5, Sonnet 5.5 and Haiku 5.5, above High) has the effort
    /// lowered to that level rather than the request refused.
    /// </summary>
    public static bool TryApplyAdaptiveGenerationShape(
        ITunableRequestBody requestBody,
        AiModel? model,
        bool shouldUseThinkingMode,
        ModelEffort? effort = null)
    {
        if (model?.IsAdaptiveThinkingModel() is not true)
        {
            return false;
        }

        requestBody.Temperature = null;
        requestBody.TopP = null;
        requestBody.TopK = null;
        requestBody.ThinkingSettings = shouldUseThinkingMode
            ? ThinkingSettings.CreateAdaptive()
            : CreateThinkingOff(model.Value);
        requestBody.OutputConfig = OutputConfig.FromEffort(model.Value.GetEffortSent(shouldUseThinkingMode, effort));

        return true;
    }

    /// <summary>
    /// The thinking setting that switches thinking off: none for always-on models, which reject
    /// every thinking-off setting, "between_tools" for models that reject "disabled".
    /// </summary>
    private static ThinkingSettings? CreateThinkingOff(AiModel model)
    {
        if (model.IsThinkingAlwaysOnModel())
        {
            return null;
        }

        return model.IsThinkingOffBetweenToolsModel()
            ? ThinkingSettings.CreateBetweenTools()
            : ThinkingSettings.CreateDisabled();
    }
}

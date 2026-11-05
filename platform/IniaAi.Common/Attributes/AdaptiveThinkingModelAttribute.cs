using IniaAi.Common.Enums;

namespace IniaAi.Common.Attributes;

/// <summary>
/// Indicates that the annotated enum entry represents a model of the adaptive-thinking
/// API generation (Claude Opus 4.7+, Sonnet 5+, Haiku 5.5, Fable 5). These models reject
/// <c>thinking: {type: "enabled", budget_tokens: N}</c> and non-default
/// <c>temperature</c>/<c>top_p</c>/<c>top_k</c> values with a 400 error.
/// </summary>
[AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public class AdaptiveThinkingModelAttribute : Attribute
{
    public const ModelEffort DEFAULT_EFFORT = ModelEffort.High;

    public bool IsAdaptiveThinkingModel { get; }

    /// <summary>
    /// True when the model cannot switch thinking off (<c>thinking: {type: "disabled"}</c>
    /// is rejected at every effort level). Applies to the Claude 5 Fable/Mythos family and to
    /// Opus 5.5.
    /// </summary>
    public bool IsThinkingAlwaysOn { get; }

    /// <summary>
    /// The effort the API applies when a request sets none. Opus 5.5 and Haiku 5.5 default to
    /// <see cref="ModelEffort.Medium"/>, one level below the rest of the generation.
    /// </summary>
    public ModelEffort DefaultEffort { get; }

    /// <summary>
    /// The highest effort the model accepts with thinking switched off. Opus 5, Sonnet 5.5 and
    /// Haiku 5.5 reject thinking-off requests above <see cref="ModelEffort.High"/> with a 400.
    /// </summary>
    public ModelEffort MaxEffortWithoutThinking { get; }

    /// <summary>
    /// True when the model rejects <c>thinking: {type: "disabled"}</c> and switches thinking
    /// off with <c>thinking: {type: "between_tools"}</c> instead. Applies to Sonnet 5.5.
    /// </summary>
    public bool IsThinkingOffBetweenTools { get; }

    public AdaptiveThinkingModelAttribute(
        bool isAdaptiveThinkingModel = true,
        bool isThinkingAlwaysOn = false,
        ModelEffort defaultEffort = DEFAULT_EFFORT,
        ModelEffort maxEffortWithoutThinking = ModelEffort.Max,
        bool isThinkingOffBetweenTools = false)
    {
        IsAdaptiveThinkingModel = isAdaptiveThinkingModel;
        IsThinkingAlwaysOn = isThinkingAlwaysOn;
        DefaultEffort = defaultEffort;
        MaxEffortWithoutThinking = maxEffortWithoutThinking;
        IsThinkingOffBetweenTools = isThinkingOffBetweenTools;
    }
}

using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

internal class ThinkingSettings : JsonSerializable<ThinkingSettings>
{
    public const string TYPE_ENABLED = "enabled";
    public const string TYPE_ADAPTIVE = "adaptive";
    public const string TYPE_DISABLED = "disabled";
    public const string TYPE_BETWEEN_TOOLS = "between_tools";
    public const string DISPLAY_SUMMARIZED = "summarized";

    [JsonProperty("type")]
    public string Type { get; set; } = TYPE_ENABLED;

    /// <summary>
    /// Only valid with <see cref="TYPE_ENABLED"/>: adaptive-generation models
    /// reject requests that carry a budget.
    /// </summary>
    [JsonProperty("budget_tokens")]
    public int BudgetTokens { get; set; } = 4000;

    /// <summary>
    /// Invalid with <see cref="TYPE_DISABLED"/> and <see cref="TYPE_BETWEEN_TOOLS"/>.
    /// Adaptive-generation models default to "omitted" (thinking blocks arrive with an
    /// empty thinking field), so requests that want readable thinking text must ask for
    /// "summarized" explicitly.
    /// </summary>
    [JsonProperty("display", NullValueHandling = NullValueHandling.Ignore)]
    public string? Display { get; set; }

    public ThinkingSettings()
    {
    }

    public ThinkingSettings(int thinkingBudget)
    {
        BudgetTokens = thinkingBudget < 1
            ? 4000
            : thinkingBudget;
    }

    public bool ShouldSerializeBudgetTokens()
    {
        return Type == TYPE_ENABLED;
    }

    public static ThinkingSettings CreateAdaptive()
    {
        return new ThinkingSettings
        {
            Type = TYPE_ADAPTIVE,
            Display = DISPLAY_SUMMARIZED
        };
    }

    public static ThinkingSettings CreateDisabled()
    {
        return new ThinkingSettings
        {
            Type = TYPE_DISABLED
        };
    }

    public static ThinkingSettings CreateBetweenTools()
    {
        return new ThinkingSettings
        {
            Type = TYPE_BETWEEN_TOOLS
        };
    }
}

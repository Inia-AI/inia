using System.Text.RegularExpressions;
using IniaAi.Application.Common.Workflows;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// Plain names for workflow, section and block fields, as the version history and the agent's
/// proposed changes show them. A path such as <c>Properties.UserPromptTemplate</c> is named by
/// its last part.
/// </summary>
public static partial class WorkflowFieldLabels
{
    private static readonly Dictionary<string, string> _named = new(StringComparer.Ordinal)
    {
        ["Notes"] = "Workflow notes",
        ["UserPromptTemplate"] = "AI instructions",
        ["SystemPromptTemplate"] = "System instructions",
        ["PromptTemplate"] = "Agent instructions",
        ["ModelIdOverride"] = "Model",
        ["ShouldUseAdvancedModel"] = "Advanced model",
        ["ShouldUseThinkingMode"] = "Thinking",
        ["ThinkingBudget"] = "Thinking budget",
        ["Effort"] = "Effort",
        ["MaxTokens"] = "Longest answer",
        ["UrlTemplate"] = "Web address",
        ["ReplacementTag"] = "Replacement tag",
        ["TextInputTemplate"] = "Section text input",
        ["OutputTitleTemplate"] = "Paragraph sub-title",
        ["SharedComponentId"] = "Library component",
        ["SharedComponentVersionHash"] = "Library version",
        ["SharedComponentLinkState"] = "Library link state",
        ["SharedComponentBindings"] = "Library slot bindings",
        [SharedComponentDefinitionDiffer.BLOCK_ORDER_FIELD] = "Block order",
    };

    private static readonly HashSet<string> _modelSettings = new(StringComparer.Ordinal)
    {
        "Model",
        "ModelIdOverride",
        "ShouldUseAdvancedModel",
        "ShouldUseThinkingMode",
        "ThinkingBudget",
        "Effort",
        "MaxTokens",
        "Temperature",
    };

    public static string Label(string? fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return string.Empty;
        }

        string tail = Tail(fieldName);
        if (_named.TryGetValue(tail, out string? named))
        {
            return named;
        }

        string spaced = WordBoundary().Replace(tail.Replace('_', ' '), " ");

        return spaced.Length <= 1
            ? spaced
            : string.Concat(spaced[..1].ToUpperInvariant(), spaced[1..].ToLowerInvariant());
    }

    /// <summary>True for a block's prompt text: the AI, system or agent instructions.</summary>
    public static bool IsPrompt(string? fieldName)
    {
        return fieldName is not null && Tail(fieldName).EndsWith("PromptTemplate", StringComparison.Ordinal);
    }

    /// <summary>True for the model, effort and thinking settings of a block.</summary>
    public static bool IsModelSetting(string? fieldName)
    {
        return fieldName is not null && _modelSettings.Contains(Tail(fieldName));
    }

    private static string Tail(string fieldName)
    {
        string tail = fieldName.Split('.')[^1];
        return string.IsNullOrWhiteSpace(tail) ? fieldName : tail;
    }

    [GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
    private static partial Regex WordBoundary();
}

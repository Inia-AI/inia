using System.Text.Json;
using IniaAi.Application.Queries.WorkflowExecutions.GetWorkflowExecution;
using IniaAi.Domain.Entities.Workflows;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// The values a past run was given, split as the run page splits them: a variable marked
/// confidential has its value masked wherever the staged run holds it, by the run page's own
/// mask, and every other value is the run's text. A value is matched to its variable by tag,
/// ignoring case, as the run page matches it.
/// </summary>
public sealed class EditorAgentRunVariables
{
    private EditorAgentRunVariables(List<string> confidential, List<string> plain)
    {
        Confidential = confidential;
        Plain = plain;
    }

    /// <summary>Every form of every confidential value: as stored, each list item, and as displayed.</summary>
    public IReadOnlyList<string> Confidential { get; }

    public IReadOnlyList<string> Plain { get; }

    public static EditorAgentRunVariables Of(IEnumerable<WorkflowVariable> definitions, IReadOnlyDictionary<string, object>? stored)
    {
        var confidential = new List<string>();
        var plain = new List<string>();
        if (stored is null || stored.Count == 0)
        {
            return new EditorAgentRunVariables(confidential, plain);
        }

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string key, object value) in stored)
        {
            values[key] = value is JsonElement element ? element : JsonSerializer.SerializeToElement(value);
        }

        var matched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowVariable definition in definitions)
        {
            JsonElement? value = WorkflowVariableDisplay.FindValue(values, definition.ReplacementTag);
            if (!WorkflowVariableDisplay.HasValue(value))
            {
                continue;
            }

            _ = matched.Add(definition.ReplacementTag);
            List<string> forms = [.. FormsOf(value!.Value), WorkflowVariableDisplay.ValueOf(definition.Type, definition.Options, value.Value)];
            (definition.IsConfidential ? confidential : plain).AddRange(forms.Where(static form => !string.IsNullOrWhiteSpace(form)));
        }

        foreach ((string key, JsonElement value) in values.Where(pair => !matched.Contains(pair.Key)))
        {
            plain.AddRange(FormsOf(value).Where(static form => !string.IsNullOrWhiteSpace(form)));
        }

        return new EditorAgentRunVariables(
            [.. confidential
                .Where(static value => value.Length >= EditorAgentReportLeaks.MIN_CONFIDENTIAL_CHARS)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(static value => value.Length)],
            [.. plain.Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>Masks every confidential value in every string of the run.</summary>
    public void Mask(JToken run)
    {
        if (Confidential.Count == 0)
        {
            return;
        }

        foreach (JValue value in run.SelectTokens("$..*").OfType<JValue>().Where(static value => value.Type == JTokenType.String).ToList())
        {
            string text = value.Value<string>()!;
            string masked = Masked(text, Confidential);
            if (!ReferenceEquals(text, masked))
            {
                value.Value = masked;
            }
        }
    }

    /// <summary>The text with every confidential value masked; the same instance when it holds none.</summary>
    public static string Masked(string text, IReadOnlyList<string> confidential)
    {
        string result = text;
        foreach (string value in confidential)
        {
            if (result.Contains(value, StringComparison.OrdinalIgnoreCase))
            {
                result = result.Replace(value, WorkflowExecutionVariableResolver.CONFIDENTIAL_MASK, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    private static IEnumerable<string> FormsOf(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            return value.EnumerateArray().SelectMany(FormsOf);
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => [value.GetString() ?? string.Empty],
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => [value.GetRawText()],
            _ => [],
        };
    }
}

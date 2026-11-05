using IniaAi.Domain.Proposals;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <param name="Overview">The whole proposal in plain words, for someone who does not know the
/// workflow; null when the agent wrote none.</param>
public sealed record EditorAgentSummaryFile(string? Overview, List<ProposalSummaryEntry> Changes);

/// <summary>
/// Reads the editor agent's summary file:
/// <c>{ "overview": ..., "changes": [ { "stableBlockId" | "section": ..., "what": ..., "feedback": [...] } ] }</c>,
/// where <c>feedback</c> names the report feedback items the change fixes and may be left out.
/// </summary>
public static class EditorAgentSummary
{
    private const string OVERVIEW = "overview";
    private const string CHANGES = "changes";
    private const string BLOCK = "stableBlockId";
    private const string SECTION = "section";
    private const string TAG = "tag";
    private const string WHAT = "what";
    private const string FEEDBACK = "feedback";

    /// <summary>Throws <see cref="JsonException"/> when the file is not that shape.</summary>
    public static EditorAgentSummaryFile Read(string summaryJson)
    {
        JObject root = WorkflowProposalBuilder.Parse(summaryJson);
        if (root[CHANGES] is not JArray changes)
        {
            throw new JsonSerializationException("summary.json has no \"changes\" list");
        }

        var entries = new List<ProposalSummaryEntry>();
        foreach (JToken item in changes)
        {
            if (item is not JObject entry)
            {
                throw new JsonSerializationException("Every entry in \"changes\" must be an object");
            }

            string? block = StringOf(entry[BLOCK]) ?? StringOf(entry[TAG]);
            string? section = StringOf(entry[SECTION]);
            string? what = StringOf(entry[WHAT]);
            if (string.IsNullOrWhiteSpace(what) || (string.IsNullOrWhiteSpace(block) && string.IsNullOrWhiteSpace(section)))
            {
                throw new JsonSerializationException(
                    "Every entry in \"changes\" needs \"what\" and either \"stableBlockId\" or \"section\"");
            }

            entries.Add(new ProposalSummaryEntry(block, section, what, FeedbackOf(entry[FEEDBACK])));
        }

        string? overview = StringOf(root[OVERVIEW])?.Trim();
        return new EditorAgentSummaryFile(string.IsNullOrEmpty(overview) ? null : overview, entries);
    }

    private static List<string>? FeedbackOf(JToken? token)
    {
        if (token is null || token.Type == JTokenType.Null)
        {
            return null;
        }

        if (token is not JArray ids || ids.Any(static id => id.Type != JTokenType.String))
        {
            throw new JsonSerializationException("\"feedback\" must be a list of feedback ids from feedback.md");
        }

        List<string> named = [.. ids.Select(static id => ((string?)id)!.Trim()).Where(static id => id.Length > 0).Distinct(StringComparer.Ordinal)];
        return named.Count == 0 ? null : named;
    }

    private static string? StringOf(JToken? token)
    {
        return token is JValue { Type: JTokenType.String } value ? (string?)value : null;
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;

namespace IniaAi.Core.Workflows.Agents;

/// <summary>
/// One citation the agent's structured output claims: the staged document it names (null when
/// the run had a single document, or when the model omitted it), the 1-based page of the FILE
/// the quoted line appears on — never the number printed on the paper, which the platform's
/// page model cannot resolve — and the line as printed. The quoted line is the anchor for
/// every format; the file page is an optional refinement that only PDFs carry.
/// </summary>
public sealed record AgentCitationClaim(string? Document, int? FilePage, string AsPrinted);

/// <summary>
/// Parses the agent output's optional top-level <c>citations</c> property. Accepted shapes,
/// liberal in structure and strict per entry: a map of field name to entry (or list of
/// entries), or a flat list of entries; each entry is either an object with a required
/// non-blank <c>as_printed</c> plus optional <c>document</c> and <c>page</c>, or a bare
/// string carrying just the quoted line. Entries that don't parse are skipped — the output
/// is model-authored and a malformed citation must never fail the block.
/// </summary>
public static class AgentCitationClaims
{
    private const string CITATIONS_PROPERTY = "citations";
    private const string AS_PRINTED_PROPERTY = "as_printed";
    private const string DOCUMENT_PROPERTY = "document";
    private const string PAGE_PROPERTY = "page";

    public static List<AgentCitationClaim> Parse(string? runOutput)
    {
        if (string.IsNullOrWhiteSpace(runOutput))
        {
            return [];
        }

        try
        {
            return ParseClaims(JsonNode.Parse(runOutput));
        }
        catch (JsonException)
        {
            return [];
        }
        catch (ArgumentException)
        {
            return [];
        }
    }

    /// <summary>
    /// Runs entirely inside Parse's catch: JsonObject materialises lazily, so duplicate JSON
    /// keys throw ArgumentException at first member access rather than at JsonNode.Parse.
    /// </summary>
    private static List<AgentCitationClaim> ParseClaims(JsonNode? root)
    {
        if (root is not JsonObject rootObject || rootObject[CITATIONS_PROPERTY] is not JsonNode citations)
        {
            return [];
        }

        var claims = new List<AgentCitationClaim>();
        switch (citations)
        {
            case JsonArray entries:
                foreach (JsonNode? entry in entries)
                {
                    AddEntry(claims, entry);
                }

                break;

            case JsonObject byField:
                foreach ((_, JsonNode? value) in byField)
                {
                    if (value is JsonArray entries)
                    {
                        foreach (JsonNode? entry in entries)
                        {
                            AddEntry(claims, entry);
                        }
                    }
                    else
                    {
                        AddEntry(claims, value);
                    }
                }

                break;

            default:
                break;
        }

        return claims;
    }

    private static void AddEntry(List<AgentCitationClaim> claims, JsonNode? entry)
    {
        switch (entry)
        {
            case JsonValue value when StringOf(value) is string quote && !string.IsNullOrWhiteSpace(quote):
                claims.Add(new AgentCitationClaim(Document: null, FilePage: null, quote.Trim()));
                break;

            case JsonObject fields:
                string? asPrinted = StringOf(fields[AS_PRINTED_PROPERTY]);
                if (string.IsNullOrWhiteSpace(asPrinted))
                {
                    return;
                }

                claims.Add(new AgentCitationClaim(
                    StringOf(fields[DOCUMENT_PROPERTY]),
                    FilePageOf(fields[PAGE_PROPERTY]),
                    asPrinted.Trim()));
                break;

            default:
                break;
        }
    }

    private static string? StringOf(JsonNode? node)
    {
        return node is JsonValue value && value.TryGetValue(out string? text) ? text : null;
    }

    private static int? FilePageOf(JsonNode? node)
    {
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue(out int page))
        {
            return page >= 1 ? page : null;
        }

        if (value.TryGetValue(out double fractional)
            && double.IsInteger(fractional)
            && fractional >= 1
            && fractional <= int.MaxValue)
        {
            return (int)fractional;
        }

        return null;
    }
}

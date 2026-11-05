using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations.Sources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Workflows.Strategies.ParallelAi;

public static class ParallelAiResponseParser
{
    public static bool TryParse(string? body, out ParallelAiParseResult result)
    {
        result = new ParallelAiParseResult();

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        JObject root;
        try
        {
            root = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return false;
        }

        JToken? output = root["output"];
        if (output is null)
        {
            return false;
        }

        JToken? content = output["content"];
        if (content is null)
        {
            return false;
        }

        if (output["basis"] is not JArray basis
            || basis.Count == 0)
        {
            return false;
        }

        List<Fact> facts = CitationFactBuilder.BuildFacts(EnumerateClaims(basis, content));
        if (facts.Count == 0)
        {
            return false;
        }

        result = new ParallelAiParseResult
        {
            CleanedBody = body,
            Facts = facts,
        };
        return true;
    }

    /// <summary>
    /// Walks <c>basis[]</c> entries, resolves each entry's <c>field</c> path
    /// into <paramref name="content"/> to recover the claim string, and pairs
    /// it with the entry's flattened <c>citations[]</c> excerpts as parent
    /// <see cref="UrlSource"/>s. Entries with missing fields, non-string
    /// resolutions, or empty citation lists still yield a pair (with empty
    /// sources) so callers see one fact per basis entry — they're filtered
    /// downstream when the broad fact has no sources.
    /// </summary>
    private static IEnumerable<(string Claim, IReadOnlyList<UrlSource> Sources)> EnumerateClaims(
        JArray basis,
        JToken content)
    {
        foreach (JToken entry in basis)
        {
            if (entry is not JObject entryObj)
            {
                continue;
            }

            string? fieldPath = entryObj["field"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(fieldPath))
            {
                continue;
            }

            JToken? resolved = ResolvePath(content, fieldPath);
            if (resolved is not JValue value || value.Type != JTokenType.String)
            {
                continue;
            }

            string? rawClaim = value.Value<string>();
            if (string.IsNullOrWhiteSpace(rawClaim))
            {
                continue;
            }

            yield return (rawClaim, BuildSourcesFromCitations(entryObj["citations"] as JArray));
        }
    }

    private static IReadOnlyList<UrlSource> BuildSourcesFromCitations(JArray? citations)
    {
        var sources = new List<UrlSource>();
        if (citations is null)
        {
            return sources;
        }

        foreach (JToken citation in citations)
        {
            if (citation is not JObject citationObj)
            {
                continue;
            }

            string? url = citationObj["url"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            if (citationObj["excerpts"] is not JArray excerpts
                || excerpts.Count == 0)
            {
                sources.Add(new UrlSource { Url = url, CitedText = string.Empty });
                continue;
            }

            foreach (JToken excerpt in excerpts)
            {
                string text = excerpt is JValue { Value: string s } ? s : string.Empty;
                sources.Add(new UrlSource { Url = url, CitedText = text });
            }
        }

        return sources;
    }

    private static JToken? ResolvePath(JToken root, string path)
    {
        JToken? current = root;
        foreach (string segment in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            if (current is JArray array)
            {
                if (int.TryParse(segment, out int index)
                    && index >= 0
                    && index < array.Count)
                {
                    current = array[index];
                }
                else
                {
                    return null;
                }
            }
            else if (current is JObject obj)
            {
                current = obj[segment];
            }
            else
            {
                return null;
            }
        }

        return current;
    }
}

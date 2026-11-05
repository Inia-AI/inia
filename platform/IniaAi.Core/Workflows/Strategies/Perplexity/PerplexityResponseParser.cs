using System.Text.RegularExpressions;
using IniaAi.Core.Common.Helpers;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Annotations.Quantities;
using IniaAi.Domain.Entities.Citations.Sources;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Workflows.Strategies.Perplexity;

public static partial class PerplexityResponseParser
{
    [GeneratedRegex(@"(?:\[\d+\])+")]
    private static partial Regex MarkerRunRegex();

    [GeneratedRegex(@"\d+")]
    private static partial Regex MarkerIndexRegex();

    [GeneratedRegex(@"[ \t]{2,}")]
    private static partial Regex HorizontalWhitespaceRunRegex();

    public static bool TryParse(string? body, out PerplexityParseResult result)
    {
        result = new PerplexityParseResult();

        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        JObject? root;
        try
        {
            root = JObject.Parse(body);
        }
        catch (JsonException)
        {
            return false;
        }

        JToken? contentToken = root["choices"]?[0]?["message"]?["content"];
        string? answer = contentToken?.Value<string>();
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        List<PerplexitySearchResult> searchResults;
        try
        {
            searchResults = root["search_results"]?.ToObject<List<PerplexitySearchResult>>() ?? [];
        }
        catch (JsonException)
        {
            return false;
        }

        if (searchResults.Count == 0)
        {
            return false;
        }

        List<Fact> facts = CitationFactBuilder.BuildFacts(EnumerateClaims(answer, searchResults));

        // Replace content in-place with the marker-stripped version; re-serialize the body.
        contentToken!.Replace(new JValue(StripMarkers(answer)));
        string cleanedBody = root.ToString(Formatting.None);

        result = new PerplexityParseResult
        {
            CleanedBody = cleanedBody,
            Facts = facts,
        };
        return true;
    }

    /// <summary>
    /// Splits the answer text on <c>[N]</c> marker runs and yields a
    /// (claim, sources) pair per chunk. The text up to the next marker run is
    /// the claim; the indices in the marker run select which
    /// <see cref="PerplexitySearchResult"/> entries become the parent sources
    /// — each one carries the search result's snippet as its
    /// <see cref="UrlSource.CitedText"/>. Text after the last marker run names no
    /// result, so it is read as an unmarked answer is; an answer with no markers is all such text.
    /// </summary>
    private static IEnumerable<(string Claim, IReadOnlyList<UrlSource> Sources)> EnumerateClaims(
        string content,
        IReadOnlyList<PerplexitySearchResult> searchResults)
    {
        int lastMarkerEnd = 0;
        foreach (Match match in MarkerRunRegex().Matches(content))
        {
            string claim = content[lastMarkerEnd..match.Index];
            lastMarkerEnd = match.Index + match.Length;

            yield return (claim, BuildSourcesForMarkerRun(match.Value, searchResults));
        }

        foreach ((string Claim, IReadOnlyList<UrlSource> Sources) tail in EnumerateUnmarkedClaims(content[lastMarkerEnd..], searchResults))
        {
            yield return tail;
        }
    }

    /// <summary>
    /// An answer written with no <c>[N]</c> markers still came from its search results. Each
    /// sentence becomes a claim sourced from the results whose snippet states one of its dressed
    /// figures as written, or one of its bare years with the same words near it; the fact builder
    /// then grounds every figure on those snippets only. Without markers every result competes,
    /// so a snippet sharing only digits ("12 staff" for "12%") or a year ("the 2024 Olympics")
    /// sources nothing.
    /// </summary>
    private static IEnumerable<(string Claim, IReadOnlyList<UrlSource> Sources)> EnumerateUnmarkedClaims(
        string content,
        IReadOnlyList<PerplexitySearchResult> searchResults)
    {
        List<(PerplexitySearchResult Entry, FigureFinder Snippet)> snippets = [.. searchResults
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Url) && !string.IsNullOrWhiteSpace(entry.Snippet))
            .Select(entry => (entry, new FigureFinder(entry.Snippet!)))];

        foreach (string sentence in TextPassage.Sentences(content))
        {
            List<Fact> figures = [.. NumericalDataExtractor.Extract(sentence).Select(value => new Fact(value) { IsNumerical = true })];
            List<Fact> dressed = [.. figures.Where(ProducingBlockCitations.IsDressedFigure)];
            List<Fact> years = [.. figures.Where(figure => figure.Quantity is { Kind: UnitKind.Year })];

            List<UrlSource> sources = [.. snippets
                .Where(snippet => dressed.Any(snippet.Snippet.States)
                    || years.Any(year => ClaimContext.SpeaksOfTheSameThing(sentence, snippet.Snippet.Text, year.Value)))
                .Select(snippet => ToUrlSource(snippet.Entry))];

            if (sources.Count > 0)
            {
                yield return (sentence, sources);
            }
        }
    }

    private static IReadOnlyList<UrlSource> BuildSourcesForMarkerRun(
        string markerRun,
        IReadOnlyList<PerplexitySearchResult> searchResults)
    {
        var sources = new List<UrlSource>();
        foreach (Match indexMatch in MarkerIndexRegex().Matches(markerRun))
        {
            if (!int.TryParse(indexMatch.Value, out int index))
            {
                continue;
            }

            if (index <= 0 || index > searchResults.Count)
            {
                continue;
            }

            PerplexitySearchResult entry = searchResults[index - 1];
            if (string.IsNullOrWhiteSpace(entry.Url))
            {
                continue;
            }

            sources.Add(ToUrlSource(entry));
        }

        return sources;
    }

    private static UrlSource ToUrlSource(PerplexitySearchResult entry)
    {
        return new UrlSource
        {
            Url = entry.Url!,
            CitedText = entry.Snippet ?? string.Empty,
        };
    }

    private static string StripMarkers(string content)
    {
        string withoutMarkers = MarkerRunRegex().Replace(content, string.Empty);
        string collapsed = HorizontalWhitespaceRunRegex().Replace(withoutMarkers, " ");
        return collapsed.Trim();
    }
}

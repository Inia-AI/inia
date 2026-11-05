using IniaAi.Core.Dto.Exa.Search;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.Citations.Sources;

namespace IniaAi.Core.Workflows.Strategies;

public static class ExaResponseParser
{
    public static List<Fact> BuildFacts(IEnumerable<ExaResultItem> results)
    {
        if (results is null)
        {
            return [];
        }

        return CitationFactBuilder.BuildFacts(EnumerateClaims(results));
    }

    private static IEnumerable<(string Claim, IReadOnlyList<UrlSource> Sources)> EnumerateClaims(
        IEnumerable<ExaResultItem> results)
    {
        foreach (ExaResultItem result in results)
        {
            if (result is null || string.IsNullOrWhiteSpace(result.Url))
            {
                continue;
            }

            string content = BuildCitableContent(result);
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            UrlSource source = new()
            {
                Url = result.Url,
                CitedText = content,
            };

            yield return (content, [source]);
        }
    }

    private static string BuildCitableContent(ExaResultItem result)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(result.Text))
        {
            parts.Add(result.Text);
        }

        string? summary = result.Summary as string
            ?? result.Summary?.ToString();
        if (!string.IsNullOrWhiteSpace(summary))
        {
            parts.Add(summary);
        }

        if (result.Highlights is not null)
        {
            foreach (string highlight in result.Highlights)
            {
                if (!string.IsNullOrWhiteSpace(highlight))
                {
                    parts.Add(highlight);
                }
            }
        }

        return string.Join("\n\n", parts);
    }
}

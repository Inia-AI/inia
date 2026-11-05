using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.WorkflowExecutions;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// A report's citations as a reader meets them, in a form two runs can compare: each paragraph's
/// text with its fact markers, each marker's value and sources, and each block's list. Ids a run
/// mints (block, page, document) are replaced by the names a reader sees.
/// </summary>
internal static partial class CitationSnapshot
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Of(IReadOnlyList<ReportParagraph> paragraphs)
    {
        Dictionary<string, string> blockNames = paragraphs
            .Where(paragraph => paragraph.BlockId is not null)
            .ToDictionary(paragraph => paragraph.BlockId!, paragraph => paragraph.Title ?? paragraph.BlockId!);

        var snapshot = paragraphs
            .OrderBy(paragraph => paragraph.Order)
            .Select(paragraph =>
            {
                Dictionary<string, string> markedValues = MarkedValues(paragraph.Content);

                return new
                {
                    block = paragraph.Title,
                    content = paragraph.Content,
                    citations = paragraph.Citations.Select(citation => new
                    {
                        id = citation.Id,
                        value = markedValues.GetValueOrDefault(citation.Id),
                        sources = citation.Sources.Select(source => Describe(source, blockNames)),
                    }),
                    citedSourcesByBlock = paragraph.CitedSourcesByBlock.Select(list => new
                    {
                        block = blockNames.GetValueOrDefault(list.BlockId, list.BlockId),
                        sources = list.Sources.Select(source => Describe(source, blockNames)),
                    }),
                };
            });

        return JsonSerializer.Serialize(snapshot, _options) + "\n";
    }

    /// <summary>The text each fact marker in <paramref name="content"/> wraps, by citation id.</summary>
    public static Dictionary<string, string> MarkedValues(string? content)
    {
        return FactMarkerRegex().Matches(content ?? string.Empty)
            .GroupBy(match => match.Groups["id"].Value)
            .ToDictionary(group => group.Key, group => group.First().Groups["value"].Value);
    }

    private static object Describe(BaseSource source, Dictionary<string, string> blockNames)
    {
        return source switch
        {
            DocumentSource document => new
            {
                type = "Document",
                document = document.DocumentName,
                page = document.PageNumber,
                citedText = document.CitedText,
            },
            UrlSource url => new { type = "Url", url = url.Url, citedText = url.CitedText },
            BlockSource block => new
            {
                type = "Block",
                block = blockNames.GetValueOrDefault(block.BlockId, block.BlockTitle ?? block.BlockId),
                citedText = block.CitedText,
                upstream = block.UpstreamSources.Select(upstream => Describe(upstream, blockNames)),
                matching = block.MatchingSources.Select(match => Describe(match, blockNames)),
            },
            _ => new { type = source.GetType().Name, citedText = source.CitedText },
        };
    }

    [GeneratedRegex("<fact id=\"(?<id>c\\d+)\">(?<value>.*?)</fact>", RegexOptions.Singleline)]
    private static partial Regex FactMarkerRegex();
}

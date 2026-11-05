using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.AiConnection.Common.ChatConnector.Enums;
using IniaAi.Common;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Documents;

namespace IniaAi.Core.Workflows.Annotations;

public interface IFactExtractor
{
    List<Fact> IdentifyAndCollectFacts(
        ResponseContentSequence responseContent,
        Dictionary<int, Page> pages);
}

public class FactExtractor : IFactExtractor
{
    public List<Fact> IdentifyAndCollectFacts(
        ResponseContentSequence responseContent,
        Dictionary<int, Page> pages)
    {
        return !responseContent.Any()
            ? []
            : ExtractFacts(
            responseContent,
            pages);
    }

    private static List<Fact> ExtractFacts(
        ResponseContentSequence responseContent,
        Dictionary<int, Page> pages)
    {
        RequestContentSequence? requestContent = responseContent.Request;

        if (requestContent is null)
        {
            return [];
        }

        List<RequestCommonContent> sourceContents = requestContent.GetSourceContents();

        List<Fact> facts = [];

        foreach (ResponseCommonContent content in responseContent)
        {
            if (content.Type is not CommonContentType.Text
                || string.IsNullOrWhiteSpace(content.TextContent)
                || content.SourceReferences is null
                || content.SourceReferences.Count == 0)
            {
                continue;
            }

            Fact fact = new(content.TextContent);

            foreach (SourceReference sourceRef in content.SourceReferences)
            {
                AddSourceReferenceToFact(fact, pages, sourceContents, sourceRef);
            }

            if (fact.Sources.Count == 0)
            {
                continue;
            }

            facts.Add(fact);
        }

        return facts;
    }

    /// <summary>
    /// Strips multiplier suffixes (million, billion, M, B, etc.) from numerical values.
    /// Returns the core number without the suffix, or null if no multiplier found.
    /// </summary>
    internal static string? StripMultiplierSuffix(string value)
    {
        foreach ((string suffix, _) in NumericalDataExtractor.MultiplierSuffixes)
        {
            // Word suffixes (starting with space) use case-insensitive matching;
            // short suffixes use exact matching and require the value to be longer than the suffix.
            bool isWordSuffix = suffix[0] == ' ';
            StringComparison comparison = isWordSuffix
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            if (value.EndsWith(suffix, comparison)
                && (isWordSuffix || value.Length > suffix.Length))
            {
                return value[..^suffix.Length];
            }
        }

        return null;
    }

    private static void AddSourceReferenceToFact(
        Fact fact,
        Dictionary<int, Page> pages,
        List<RequestCommonContent> sourceContents,
        SourceReference sourceRef)
    {
        if (sourceRef.CitedText is null)
        {
            return;
        }

        int index = sourceRef.Index;

        if (index < 0 || index >= sourceContents.Count)
        {
            return;
        }

        RequestCommonContent sourceContent = sourceContents[index];

        if (sourceContent.IsBlockReference
            && sourceContent.ReferenceId is not null)
        {
            fact.Sources.Add(new BlockSource
            {
                CitedText = sourceRef.CitedText,
                BlockId = sourceContent.ReferenceId,
                BlockTitle = sourceContent.Title
            });

            return;
        }

        if (!sourceContent.IsInputContext
            || sourceContent.ReferenceId is null)
        {
            return;
        }

        Page? page = pages
            .Where(p => p.Value.Id == sourceContent.ReferenceId)
            .Select(p => p.Value)
            .FirstOrDefault();

        if (page is null)
        {
            return;
        }

        if (page.SourceUrl is not null)
        {
            fact.Sources.Add(new UrlSource
            {
                CitedText = PageFormatting.RemovePageMarkers(sourceRef.CitedText),
                Url = page.SourceUrl
            });

            return;
        }

        fact.Sources.Add(new DocumentSource
        {
            CitedText = PageFormatting.RemovePageMarkers(sourceRef.CitedText),
            DocumentId = page.DocumentId,
            PageId = page.Id,
            PageNumber = page.PageNumber,
            FilePageNumber = page.FilePageNumber,
            DocumentName = page.Document?.FileName ?? string.Empty
        });
    }
}

using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common;
using IniaAi.Common.Serialization;
using IniaAi.Core.Rag.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Tools.RagStrategies;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Rag.Strategies;

internal class UseAutoDetectedTableOfContentsStrategy : IRagStrategy
{
    private UseAutoDetectedTableOfContentsPrompts _prompts = new();

    public async Task<Dictionary<string, Dictionary<int, Page>>> ExecuteFilteringAsync(
        RagExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _prompts = context.UseAutoDetectedTableOfContentsPrompts ?? new();

        RagSettingsExecutionState executionState = context.RagSettings.ExecutionState
            ?? new RagSettingsExecutionState(context.RagSettings);

        string prompt = string.Format(
            _prompts.RelevantSectionsIdentificationPrompt,
            context.Topics,
            executionState.TopicsQuery);

        ResponseContentSequence relevantSectionsIdentificationResponseContent =
            await context.ChatConnector.GetResponseAsync(
                prompt,
                shouldUseAdvancedModel: true,
                cancellationToken: cancellationToken);

        context.AccumulateTokens(relevantSectionsIdentificationResponseContent);

        string relevantSectionsIdentificationResponse =
            relevantSectionsIdentificationResponseContent.GetPlainText()
            ?? string.Empty;

        context.Logger.LogTrace(
            "Selecting topics response: {Length} characters",
            relevantSectionsIdentificationResponse.Length);

        var sectionSelection = SectionSelection.FromJson(relevantSectionsIdentificationResponse);

        if (sectionSelection is null)
        {
            context.Logger.LogWarning("Failed to parse section selection from response.");

            return [];
        }

        var toc = TableOfContents.FromString(context.Topics);

        if (toc is null)
        {
            context.Logger.LogWarning("Failed to parse table of contents from string.");

            return [];
        }

        var selectedSelection = sectionSelection.RelevantSections
            .Select(rs => rs.SectionName)
            .ToList();

        List<int> pagesFromSelectedSections = toc.GetPageNumbersForSections(
            selectedSelection,
            context.Pages.Max(p => p.PageNumber));

        string? pagesFromTopicsText =
            GetTextFromPages(context.Pages, pagesFromSelectedSections);

        if (string.IsNullOrEmpty(pagesFromTopicsText))
        {
            context.Logger.LogWarning("Failed to filter pages from TOC.");

            return [];
        }

        context.Logger.LogTrace(
            "Pages from TOC: {PageCount} pages",
            pagesFromSelectedSections.Count);

        string selectedPagesAnalysisPrompt = string.Format(
            _prompts.RelevanceAssessmentPrompt,
            pagesFromTopicsText,
            executionState.TopicsQuery);

        ResponseContentSequence selectedPagesAnalysisResponseContent =
            await context.ChatConnector.GetResponseAsync(
                selectedPagesAnalysisPrompt,
                shouldUseAdvancedModel: true,
                cancellationToken: cancellationToken);

        context.AccumulateTokens(selectedPagesAnalysisResponseContent);

        string selectedPagesAnalysisResponse =
            selectedPagesAnalysisResponseContent.GetPlainText()
            ?? string.Empty;

        context.Logger.LogTrace(
            "Selected pages analysis response: {Length} characters",
            selectedPagesAnalysisResponse.Length);

        string exactPagesExtractionPrompt = string.Format(
            _prompts.PageSelectionPrompt,
            selectedPagesAnalysisResponse);

        ResponseContentSequence exactPagesExtractionResponseContent =
            await context.ChatConnector.GetResponseAsync(
                exactPagesExtractionPrompt,
                shouldUseAdvancedModel: true,
                cancellationToken: cancellationToken);

        context.AccumulateTokens(exactPagesExtractionResponseContent);

        string exactPagesExtractionResponse =
            exactPagesExtractionResponseContent.GetPlainText()
            ?? string.Empty;

        context.Logger.LogTrace(
            "Exact pages extraction response: {Length} characters",
            exactPagesExtractionResponse.Length);

        string? exactPages = Utils.GetStringFromTag(
                exactPagesExtractionResponse,
                "answer")?
            .Replace(" ", "")
            .Replace("page", "");

        if (string.IsNullOrWhiteSpace(exactPages))
        {
            context.Logger.LogWarning(
                "Exact pages extraction response does not contain an answer.");

            return [];
        }

        List<int> exactPageNumbers = TryParseExactPagesForNumbers(
            context.Logger,
            exactPages);

        Dictionary<int, Page> indexedPages = [];

        foreach (int index in exactPageNumbers.Order())
        {
            if (index > 0 && index <= context.Pages.Count)
            {
                indexedPages[index - 1] = context.Pages[index - 1];
            }
        }

        return new Dictionary<string, Dictionary<int, Page>>
        {
            { string.Empty, indexedPages }
        };
    }

    private static List<int> TryParseExactPagesForNumbers(ILogger logger, string exactPages)
    {
        try
        {
            return Utils.ParseStringForNumbersList(exactPages);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Failed to parse exact pages for numbers from {Length} characters",
                exactPages.Length);

            return [];
        }
    }

    internal static string? GetTextFromPages(List<Page> pages, List<int> pagesFromTopics)
    {
        string? result = null;

        foreach (int index in pagesFromTopics)
        {
            Page page = pages[index - 1];

            if (!string.IsNullOrEmpty(result))
            {
                result +=
                $$"""


                <page{{index}}>
                {{page.ReadableText}}
                </page{{index}}>
                """;
            }
            else
            {
                result =
                $$"""
                <page{{index}}>
                {{page.ReadableText}}
                </page{{index}}>
                """;
            }
        }

        return result;
    }

    private class SectionSelection : JsonSerializable<SectionSelection>
    {
        public List<RelevantSection> RelevantSections { get; set; } = [];
        public AnalysisDetails AnalysisDetails { get; set; } = new AnalysisDetails();
    }

    private class AnalysisDetails : JsonSerializable<AnalysisDetails>
    {
        public List<string> QueryKeywords { get; set; } = [];
        public string OverallReasoning { get; set; } = "";
        public string SectionRelationships { get; set; } = "";
        public int ConfidenceLevel { get; set; } = 0;
    }

    private class RelevantSection : JsonSerializable<RelevantSection>
    {
        public string SectionName { get; set; } = "";
        public int RelevanceScore { get; set; } = 0;
        public string RelevanceJustification { get; set; } = "";
    }

    public class TableOfContents
    {
        private readonly Dictionary<string, int> _sections = [];

        public IReadOnlyDictionary<string, int> Sections => _sections;

        public List<int> GetPageNumbersForSections(IEnumerable<string> sections, int lastPage = int.MaxValue)
        {
            List<int> pageNumbers = [];

            foreach ((string section, int startPage) in _sections.OrderBy(kvp => kvp.Value))
            {
                if (!sections.Contains(section))
                {
                    continue;
                }

                KeyValuePair<string, int> nextSection = _sections
                    .SkipWhile(kvp => kvp.Key != section)
                    .Skip(1)
                    .FirstOrDefault();

                int endPage = nextSection.Key is not null ? nextSection.Value : lastPage;

                if (endPage == int.MaxValue)
                {
                    throw new InvalidOperationException("Last page must be specified when the last section is selected.");
                }

                for (int i = startPage; i <= endPage; i++)
                {
                    pageNumbers.Add(i);
                }
            }

            return [.. pageNumbers.Distinct().Order()];
        }

        public static TableOfContents? FromString(string? tocString)
        {
            if (string.IsNullOrWhiteSpace(tocString))
            {
                return null;
            }

            var toc = new TableOfContents();

            using var reader = new StringReader(tocString);
            string? line;

            while ((line = reader.ReadLine()) is not null)
            {
                string[] parts = line.Split(':');

                if (parts.Length != 2)
                {
                    continue;
                }

                string sectionTitle = parts[0].Trim();
                int pageNumber = int.Parse(parts[1].Trim());

                toc._sections[sectionTitle] = pageNumber;
            }

            return toc;
        }
    }
}

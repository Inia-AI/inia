using IniaAi.AiConnection.Abstractions;
using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Core.Dto.Exa.Search;
using IniaAi.Core.Dto.Exa.Search.Enums;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Responses;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Text;

namespace IniaAi.Core.Workflows.Strategies;

public class ExaSearchStrategy : IBlockStrategy
{
    public const string FILE_TAG_PREFIX = "exa-search-results";
    public const int DEFAULT_MAX_PAGES_PER_RESULT = 100;

    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BlockStrategyOutput output = new();

        IntelligentSearchProperties props = block.Properties as IntelligentSearchProperties
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(IntelligentSearchProperties)}");

        block.ExecutionState ??= new (block);
        props.ExecutionState ??= new (props);

        if (props.ExecutionState.SearchTerm is null
            || props.ExecutionState.SearchTerm.IsInvalidBlockProperty(context.PlatformSettings.IniaAi.NoDataForPlaceHolderText))
        {
            context.Logger.LogWarning(
                "{block} No search term available",
                block);

            // Nothing to search is the upstream block having no data, not a failure.
            block.ExecutionState.MarkAsSkipped("No search term available");

            return output;
        }

        string searchTerm = props.ExecutionState.SearchTerm;

        ExaSearchRequest request = CreateExaSearchRequest(props, props.ExecutionState, searchTerm);

        try
        {
            ExaSearchResponse? response = await context.ExaSearchService
                .SearchAsync(request, cancellationToken);

            int maxPagesPerResult = props.MaxPagesPerResult ?? DEFAULT_MAX_PAGES_PER_RESULT;

            string answer = FormatSearchResults(
                request,
                response,
                block.ReplacementTag,
                props.ExecutionState.CreatesDocumentClass,
                context.PlatformSettings.IniaAi.WordCountPerPage,
                maxPagesPerResult,
                output,
                context.IsCitationSource ? context.BlockAnnotations : null);

            block.BlockResponse = new BlockResponse(block)
            {
                RawResponse = answer
            };

            context.Logger.LogTrace(
                "{block} response: {Length} characters",
                block,
                block.BlockResponse.RawResponse?.Length ?? 0);
        }
        catch (Exception ex)
        {
            context.Logger.LogError(ex,
                "{block} Error executing Exa search for a term of {Length} characters",
                block,
                searchTerm.Length);

            block.BlockResponse = new BlockResponse(block)
            {
                RawResponse = $"{IChatConnector.ERROR_PREFIX}Error executing search: {ex.Message}"
            };

            block.ExecutionState.Error = ex.Message;
        }

        return output;
    }

    private static string FormatSearchResults(
        ExaSearchRequest request,
        ExaSearchResponse? response,
        string blockTag,
        string? documentClass,
        int wordCountPerPage,
        int maxPagesPerResult,
        BlockStrategyOutput output,
        BlockAnnotations? blockAnnotations)
    {
        if (response?.Results is null || response.Results.Length == 0)
        {
            return "No search results found";
        }

        wordCountPerPage = Math.Max(wordCountPerPage, 1);

        List<(ExaResultItem result, List<string> pages)> filteredWithPages = [];
        foreach (ExaResultItem result in GetAllResults(response.Results))
        {
            List<string> pages = GetPaginatedText(result, wordCountPerPage);
            if (pages.Count <= maxPagesPerResult)
            {
                filteredWithPages.Add((result, pages));
            }
        }

        if (filteredWithPages.Count == 0)
        {
            return "No search results found";
        }

        List<ExaResultItem> allResults = [.. filteredWithPages.Select(x => x.result)];

        string answer = string.Empty;
        if (request.Contents?.Text is true or ExaTextConfig)
        {
            answer += "<searchResults>\n\n";

            foreach (ExaResultItem result in allResults)
            {
                if (result.Url is null || result.Text is null)
                {
                    continue;
                }

                answer +=
                $"""
                URL: {result.Url}
                TITLE: {result.Title}
                TEXT:
                {Utils.CleanWhitespace(result.Text)}


                """;
            }

            answer += "</searchResults>\n\n";
        }

        if (request.Contents?.Summary is true or ExaSummaryConfig)
        {
            answer += "<searchSummaries>\n\n";

            foreach (ExaResultItem result in allResults)
            {
                if (result.Url is null || result.Summary is null)
                {
                    continue;
                }

                answer +=
                $"""
                URL: {result.Url}
                TITLE: {result.Title}
                SUMMARY:
                {(result.Summary is string summary ? Utils.CleanWhitespace(summary) : result.Summary)}


                """;
            }

            answer += "</searchSummaries>\n\n";
        }

        if (request.Contents?.Highlights is true)
        {
            answer += "<searchHighlights>\n\n";

            foreach (ExaResultItem result in allResults)
            {
                if (result.Url is null || result.Highlights is null)
                {
                    continue;
                }

                answer +=
                $"""
                URL: {result.Url}
                TITLE: {result.Title}
                HIGHLIGHTS:
                {string.Join("\n\n", result.Highlights.Select(Utils.CleanWhitespace))}


                """;
            }

            answer += "</searchHighlights>\n\n";
        }

        if (string.IsNullOrWhiteSpace(answer))
        {
            return "No search results found";
        }

        blockAnnotations?.AddFacts(ExaResponseParser.BuildFacts(allResults));

        if (documentClass is not null)
        {
            CreateDocumentFromExaResults(
                blockTag,
                documentClass,
                output,
                filteredWithPages,
                answer);
        }

        return answer.Trim();
    }

    private static void CreateDocumentFromExaResults(
        string blockTag,
        string documentClass,
        BlockStrategyOutput output,
        List<(ExaResultItem result, List<string> pages)> filteredResults,
        string answer)
    {
        byte[] answerBytes = Encoding.UTF8.GetBytes(answer);
        var fileMetadata = new FileMetadata("", $"{FILE_TAG_PREFIX}-{blockTag}.txt", answerBytes);
        var document = new Document(fileMetadata, MediaType.TextPlain);

        int pageNumber = 1;

        foreach ((ExaResultItem result, List<string> pageTexts) in filteredResults)
        {
            foreach (string pageText in pageTexts)
            {
                _ = new Page(document, pageNumber, filePageNumber: 1) // no pages in text documents
                {
                    Text = pageText,
                    SourceUrl = result.Url,
                };
                pageNumber++;
            }
        }

        var documentInfo = new DocumentInfo(document.Id)
        {
            Document = document,
            DocumentClass = documentClass
                ?? string.Empty,
            ShouldBeDisplayedInOutput = false,
        };

        output.Documents.Add(documentInfo);
    }

    private static List<string> GetPaginatedText(ExaResultItem result, int wordCountPerPage)
    {
        List<string> pages = [];

        string? summaryText = result.Summary is string summary
            ? Utils.CleanWhitespace(summary)
            : null;

        string? resultText = string.IsNullOrWhiteSpace(result.Text)
            ? string.IsNullOrWhiteSpace(summaryText)
                ? null
                : summaryText
            : result.Text;

        if (string.IsNullOrWhiteSpace(resultText))
        {
            return pages;
        }

        string text = result.Title is not null
            ? $"{result.Title}\n\n{resultText}"
            : resultText;

        while (!string.IsNullOrWhiteSpace(text))
        {
            string chunk = Utils.GetFirstNWords(text, wordCountPerPage);
            pages.Add(chunk);
            text = text.Length > chunk.Length
                ? text[chunk.Length..].TrimStart()
                : string.Empty;
        }

        return pages;
    }

    private static IEnumerable<ExaResultItem> GetAllResults(ExaResultItem[] results)
    {
        foreach (ExaResultItem result in results)
        {
            yield return result;

            if (result.Subpages is not null)
            {
                foreach (ExaResultItem subpage in result.Subpages)
                {
                    yield return subpage;
                }
            }
        }
    }

    private static ExaSearchRequest CreateExaSearchRequest(
        IntelligentSearchProperties props,
        IntelligentSearchPropertiesExecutionState state,
        string searchTerm)
    {
        ExaSearchType searchType = ExaSearchType.Auto;
        if (state.SearchType is not null
            && Utils.TryParseEnumFromEnumMemberAttribute(state.SearchType, out ExaSearchType propsSearchType))
        {
            searchType = propsSearchType;
        }

        string[]? excludeDomains = state.ExcludeDomains.Count > 0
            ? [.. state.ExcludeDomains]
            : null;

        string[]? includeDomains = state.IncludeDomains.Count > 0
            ? [.. state.IncludeDomains]
            : null;

        ExaSearchCategory? category = null;
        if (state.Category is not null
            && Utils.TryParseEnumFromEnumMemberAttribute(state.Category, out ExaSearchCategory propsCategory))
        {
            category = propsCategory;
        }

        ExaLiveCrawlStrategy? liveCrawlStrategy = null;
        if (state.LiveCrawlStrategy is not null
            && Utils.TryParseEnumFromEnumMemberAttribute(state.LiveCrawlStrategy, out ExaLiveCrawlStrategy propsLiveCrawl))
        {
            liveCrawlStrategy = propsLiveCrawl;
        }

        ExaContentsConfig contents = CreateExaContentsConfig(includeDomains, props, state);

        var request = new ExaSearchRequest
        {
            Query = searchTerm,
            NumResults = props.NumResults,
            Type = searchType,
            Contents = contents,
            ExcludeDomains = excludeDomains,
            IncludeDomains = includeDomains,
            StartPublishedDate = state.StartPublishedDate,
            EndPublishedDate = state.EndPublishedDate,
            Category = category,
            LiveCrawl = liveCrawlStrategy,
            LiveCrawlTimeout = props.LiveCrawlTimeout,
        };

        return request;
    }

    private static ExaContentsConfig CreateExaContentsConfig(
        string[]? includeDomains,
        IntelligentSearchProperties props,
        IntelligentSearchPropertiesExecutionState state)
    {
        ExaTextConfig? textConfig = CreateExaTextConfig(props);
        ExaSummaryConfig? summary = CreateExaSummaryConfig(state);

        int? subpages = props.Subpages is not null
                ? props.Subpages.Value
                : (includeDomains is not null && includeDomains.Length > 0)
                    ? 3
                    : null;

        string[]? subpageTarget = state.SubpageTarget.Count > 0
            ? [.. state.SubpageTarget]
            : null;

        return new ExaContentsConfig
        {
            Text = textConfig is not null ? textConfig : props.TextEnabled,
            Highlights = props.HighlightsEnabled,
            Summary = summary is not null ? summary : props.SummaryEnabled,
            Subpages = subpages,
            SubpageTarget = subpageTarget,
        };
    }

    private static ExaTextConfig? CreateExaTextConfig(
        IntelligentSearchProperties props)
    {
        return props.MaxCharacters is null && props.IncludeHtmlTags is null
            ? null
            : new ExaTextConfig()
            {
                MaxCharacters = props.MaxCharacters,
                IncludeHtmlTags = props.IncludeHtmlTags,
            };
    }

    private static ExaSummaryConfig? CreateExaSummaryConfig(
        IntelligentSearchPropertiesExecutionState state)
    {
        Dictionary<string, object>? schema = null;
        if (state.SummarySchema is not null)
        {
            schema = JsonConvert.DeserializeObject<Dictionary<string, object>>(state.SummarySchema);
        }

        return string.IsNullOrWhiteSpace(state.SummaryQuery) && schema is null
            ? null
            : new ExaSummaryConfig()
            {
                Query = state.SummaryQuery,
                Schema = schema,
            };
    }
}

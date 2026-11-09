using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common;
using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Configuration.Enums.Common;
using IniaAi.Core.Documents;
using IniaAi.Domain.Entities.DocumentAnalysis;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.FileFormats.Pdf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Text;

namespace IniaAi.Core.Services;

public partial class DocumentAnalysisService : ITokenAccumulator
{
    private const string TOPIC_LABEL = "TOPIC:";
    private const string PAGES_LABEL = "Pages:";

    #region Private Fields

    private readonly PlatformSettings _settings;
    private readonly ILogger<DocumentAnalysisService> _logger;
    private readonly IChatConnector _chatConnector;
    private readonly TenantModelFamilyResolver _tenantModelFamilyResolver;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IFileStorageManager _fileStorageManager;
    private readonly IPdfFormat _pdfFormat;
    private readonly ModuleWorkflowTypes _moduleWorkflowTypes;
    private readonly List<EmbeddingVector> _orphanedEmbeddingVectors = [];

    private bool _shouldUseAdvancedModel => _settings.IniaAi.SummarizationModelType is ModelTier.Advanced;

    // Token accumulation for tracking (thread-safe for parallel calls)
    private int _accumulatedInputTokens;
    private int _accumulatedOutputTokens;

    #endregion

    public IReadOnlyList<EmbeddingVector> OrphanedEmbeddingVectors => _orphanedEmbeddingVectors;

    #region ITokenAccumulator Implementation

    /// <inheritdoc />
    public int AccumulatedInputTokens => _accumulatedInputTokens;

    /// <inheritdoc />
    public int AccumulatedOutputTokens => _accumulatedOutputTokens;

    /// <inheritdoc />
    public int AccumulatedCacheCreationTokens => 0;

    /// <inheritdoc />
    public int AccumulatedCacheReadTokens => 0;

    /// <inheritdoc />
    public string? LastUsedModelId { get; private set; }

    /// <inheritdoc />
    public void ResetTokenAccumulator()
    {
        _ = Interlocked.Exchange(ref _accumulatedInputTokens, 0);
        _ = Interlocked.Exchange(ref _accumulatedOutputTokens, 0);
        LastUsedModelId = null;
        _orphanedEmbeddingVectors.Clear();
    }

    private void AccumulateTokens(ResponseContentSequence response)
    {
        _ = Interlocked.Add(ref _accumulatedInputTokens, response.InputTokens);
        _ = Interlocked.Add(ref _accumulatedOutputTokens, response.OutputTokens);

        // Capture model ID from response metadata
        if (response.Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? modelValue)
            && modelValue is string modelString
            && !string.IsNullOrWhiteSpace(modelString))
        {
            LastUsedModelId = modelString;
        }
    }

    #endregion

    #region Constructors

    public DocumentAnalysisService(
        PlatformSettings settings,
        ILogger<DocumentAnalysisService> logger,
        IChatConnector chatConnector,
        TenantModelFamilyResolver tenantModelFamilyResolver,
        IServiceScopeFactory serviceScopeFactory,
        IFileStorageManager fileStorageManager,
        IPdfFormat pdfFormat,
        ModuleWorkflowTypes? moduleWorkflowTypes = null)
    {
        _settings = settings;
        _moduleWorkflowTypes = moduleWorkflowTypes ?? new ModuleWorkflowTypes([]);
        _logger = logger;
        _chatConnector = chatConnector;
        _tenantModelFamilyResolver = tenantModelFamilyResolver;
        _serviceScopeFactory = serviceScopeFactory;
        _fileStorageManager = fileStorageManager;
        _pdfFormat = pdfFormat;
    }

    public DocumentAnalysisService(
        IOptions<PlatformSettings> options,
        ILogger<DocumentAnalysisService> logger,
        IChatConnector chatConnector,
        TenantModelFamilyResolver tenantModelFamilyResolver,
        IServiceScopeFactory serviceScopeFactory,
        IFileStorageManager fileStorageManager,
        IPdfFormat pdfFormat,
        ModuleWorkflowTypes moduleWorkflowTypes) : this(
            options.Value,
            logger,
            chatConnector,
            tenantModelFamilyResolver,
            serviceScopeFactory,
            fileStorageManager,
            pdfFormat,
            moduleWorkflowTypes)
    {
    }

    #endregion

    #region Public Methods

    public async Task<string?> GenerateTopicsFromOverviewsAsync(
        Dictionary<int, Page> pages,
        string? organizationId,
        string? topicsModel = null,
        CancellationToken cancellationToken = default)
    {
        if (pages.Count == 0)
        {
            _logger.LogInformation("No pages to generate topics for");

            return null;
        }

        string overviews = PagesHelper.GetOverviewsFromPages(pages);
        TopicsPrompts prompts = await new TopicsPromptsLoader(_logger, _serviceScopeFactory).LoadAsync().ConfigureAwait(false);

        string? topics = string.IsNullOrWhiteSpace(topicsModel)
            ? await GenerateTopicsFromOverviewsAsync(
                overviews,
                organizationId,
                prompts,
                cancellationToken).ConfigureAwait(false)
            : topicsModel;

        if (topics is null)
        {
            _logger.LogWarning("Topics response does not contain an answer.");

            return null;
        }

        string? topicGroupingAnswer = await GenerateTopicGroupingsAsync(
            overviews,
            topics,
            organizationId,
            prompts,
            cancellationToken).ConfigureAwait(false);

        if (topicGroupingAnswer is null)
        {
            _logger.LogWarning("Topic grouping response does not contain an answer.");

            return null;
        }

        return topicGroupingAnswer;
    }

    public string? GenerateTocUsingModelToc(
        List<Page> pages,
        string modelToc)
    {
        List<string> sectionTitles = [.. modelToc.Split(new string("\n\r").ToCharArray(), StringSplitOptions.RemoveEmptyEntries)];

        Dictionary<string, string> tocSectionTitles = [];

        foreach (string sectionTitle in sectionTitles)
        {
            _logger.LogDebug("Looking for a page for {SectionTitle}", sectionTitle);

            foreach (Page page in pages.OrderBy(p => p.PageNumber))
            {
                if (page.Text is null)
                {
                    continue;
                }

                if (page.Text.Contains(sectionTitle, StringComparison.OrdinalIgnoreCase))
                {
                    page.SectionTitles = sectionTitle;
                    tocSectionTitles[sectionTitle] = page.PageNumber.ToString();

                    _logger.LogDebug(
                        "Found a page for {SectionTitle}: {PageNumber}",
                        sectionTitle,
                        page.PageNumber);

                    break;
                }
            }
        }

        string toc = string.Empty;

        foreach (KeyValuePair<string, string> sectionTitle in tocSectionTitles)
        {
            toc += $"{sectionTitle.Key}: {sectionTitle.Value}\n";
        }

        return toc.Trim();
    }

    public async Task GenerateOverviewsSlidingWindowAsync(
        List<Page> pages,
        string organizationId,
        string? systemPrompt,
        string? userPromptTemplate,
        int contextWindow = 1,
        bool? shouldUseAdvancedModel = null,
        CancellationToken cancellationToken = default)
    {
        bool useAdvancedModel = shouldUseAdvancedModel ?? _shouldUseAdvancedModel;

        var pagesToSummarize = pages
            .Where(PageRequiresOverview)
            .Where(HasSummarizableContent)
            .ToList();

        _logger.LogInformation(
            "Sliding window summarization: {Total} pages: {Empty} empty, {Valid} summarized, {Erroneous} erroneous, {ToSummarize} to summarize.",
            pages.Count,
            pages.Count(p => string.IsNullOrWhiteSpace(p.Text)),
            pages.Count(IsOverviewValid),
            pages.Count(IsOverviewErroneous),
            pagesToSummarize.Count);

        if (pagesToSummarize.Count == 0)
        {
            _logger.LogInformation("No pages need summarization.");
            return;
        }

        var totalStopwatch = Stopwatch.StartNew();

        int llmMax = _settings?.IniaAi.LlmMaxDegreeOfParallelism
            ?? Environment.ProcessorCount;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = llmMax,
            CancellationToken = cancellationToken
        };

        var model = _tenantModelFamilyResolver.GetModelOverride(organizationId, useAdvancedModel);
        ModelFamily family = _tenantModelFamilyResolver.GetModelFamilyForFunction(organizationId, FunctionType.DocumentProcessing);

        List<Page> imageOnlyPages = [.. pagesToSummarize.Where(page => page.IsOcrImageOnly)];

        if (imageOnlyPages.Count > 0)
        {
            await PagesHelper.EnsureImageOnlyPageImagesAsync(
                imageOnlyPages,
                _pdfFormat,
                _fileStorageManager,
                _logger,
                cancellationToken);
        }

        int unclosedAnswers = 0;

        await Parallel.ForEachAsync(
            pagesToSummarize, options,
            async (page, ct) =>
            {
                ct.ThrowIfCancellationRequested();

                var pageStopwatch = Stopwatch.StartNew();
                ResponseContentSequence responseContent;

                var userPrompt = string.Format(userPromptTemplate ?? string.Empty, page.PageNumber);

                if (page.IsOcrImageOnly)
                {
                    try
                    {
                        await PagesHelper.EnsurePageImageAsync(page, _pdfFormat, _fileStorageManager, _logger, ct);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(
                            ex,
                            "Cannot generate image overview for page {PageNumber}: image rehydration failed.",
                            page.PageNumber);
                        return;
                    }

                    BinaryContent? pageImage = page.Image;
                    if (pageImage is null)
                    {
                        _logger.LogWarning(
                            "Cannot generate image overview for page {PageNumber}: image unavailable.",
                            page.PageNumber);
                        return;
                    }

                    RequestContentSequence imageContent = [];
                    imageContent.Add(userPrompt);
                    imageContent.Add(pageImage);

                    responseContent = await _chatConnector.GetResponseAsync(
                        imageContent,
                        systemPrompt,
                        shouldUseAdvancedModel: useAdvancedModel,
                        model: model,
                        family: family,
                        cancellationToken: ct);
                }
                else
                {
                    int index = pages.IndexOf(page);

                    int start = Math.Max(index - contextWindow, 0);
                    int end = Math.Min(index + contextWindow, pages.Count - 1);

                    List<Page> windowPages = pages.GetRange(start, end - start + 1);

                    var sb = new StringBuilder();
                    foreach (Page p in windowPages)
                    {
                        if (string.IsNullOrWhiteSpace(p.Text))
                        {
                            continue;
                        }

                        _ = sb.AppendLine($"<page{p.PageNumber}>");
                        _ = sb.AppendLine(p.Text);
                        _ = sb.AppendLine($"</page{p.PageNumber}>");
                        _ = sb.AppendLine();
                    }

                    string windowText = sb.ToString();

                    responseContent = await _chatConnector.GetResponseAsync(
                        $"""
                        {windowText}

                        {userPrompt}
                        """,
                        systemPrompt,
                        shouldUseAdvancedModel: useAdvancedModel,
                        model: model,
                        family: family,
                        cancellationToken: ct);
                }

                AccumulateTokens(responseContent);

                string response = responseContent.GetPlainText() ?? string.Empty;

                string? content = ReadOverviewAnswer(response, out bool answerClosed)?
                    .Replace($"Page {page.PageNumber}:", "")
                    .Trim();

                if (!answerClosed)
                {
                    _ = Interlocked.Increment(ref unclosedAnswers);
                }

                if (!string.IsNullOrWhiteSpace(content))
                {
                    page.Overview = content;

                    _logger.LogInformation(
                        "Parsed overview for page {PageNumber}: {Length} characters. Took {Ms} ms",
                        page.PageNumber,
                        content.Length,
                        pageStopwatch.ElapsedMilliseconds);
                }
                else
                {
                    _logger.LogWarning(
                        "Could not parse valid answer for page {PageNumber}. Raw response: {Length} characters",
                        page.PageNumber,
                        response.Length);
                }
            });

        if (unclosedAnswers > 0)
        {
            _logger.LogWarning(
                "{Unclosed} of {Count} page overviews of document {DocumentId} never closed their answer tag; each kept the reply up to a second opening tag or its end. Check the answer format of the overview prompt.",
                unclosedAnswers,
                pagesToSummarize.Count,
                pagesToSummarize[0].DocumentId);
        }

        _logger.LogInformation(
            "Sliding window summarization complete. Took {Elapsed} ms for {Count} pages.",
            totalStopwatch.ElapsedMilliseconds,
            pagesToSummarize.Count);
    }

    /// <summary>
    /// Summarises the document's pages with the prompt its class resolves to: the class's own, then
    /// the workflow's default. Without either the pages get no overview, so they get no embedding
    /// either, and no model is called; overviews left by an earlier prompt are cleared.
    /// </summary>
    public async Task GenerateOverviewsAsync(
        Document document,
        string organizationId,
        PageOverviewPrompts? classPrompts,
        PageOverviewPrompts? workflowDefault,
        string? workflowTypeId = null,
        CancellationToken cancellationToken = default)
    {
        bool shouldProcessPageByPage = !_moduleWorkflowTypes.SpreadsheetOverviewsReadNeighbouringPages(workflowTypeId)
            && (document.MediaType is MediaType.ApplicationVndMsExcel
                or MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet
                or MediaType.ApplicationVndMsExcelSheetMacroEnabled12
                or MediaType.TextCsv);

        int contextWindow = shouldProcessPageByPage ? 0 : 1;

        PageOverviewPrompt? prompt = PageOverviewPrompts.Resolve(classPrompts, workflowDefault, singlePage: contextWindow == 0);
        string currentHash = prompt is null
            ? NO_OVERVIEW_PROMPT_HASH
            : ComputeOverviewPromptHash(prompt.SystemPrompt, prompt.UserPrompt);

        if (document.OverviewPromptHash != currentHash)
        {
            _logger.LogInformation(
                "Overview prompt hash changed for document {DocumentId}. Old: {OldHash}, New: {NewHash}. Clearing page overviews for regeneration.",
                document.Id,
                document.OverviewPromptHash ?? "(null)",
                currentHash);

            foreach (Page page in document.Pages)
            {
                if (page.EmbeddingVector is not null)
                {
                    _orphanedEmbeddingVectors.Add(page.EmbeddingVector);
                }

                page.Overview = null;
                page.EmbeddingVector = null;
                page.EmbeddingVectorId = null;
            }
        }

        document.OverviewPromptHash = currentHash;

        if (prompt is null)
        {
            _logger.LogInformation(
                "Document {DocumentId} gets no page overviews: neither its document class nor its workflow has an overview prompt.",
                document.Id);

            return;
        }

        await GenerateOverviewsSlidingWindowAsync(
            [.. document.Pages.OrderBy(p => p.PageNumber)],
            organizationId,
            prompt.SystemPrompt,
            prompt.UserPrompt,
            contextWindow,
            prompt.ShouldUseAdvancedModel,
            cancellationToken);
    }

    public void GenerateTocUsingModelToc(Document document, string modelToc)
    {
        string? toc = GenerateTocUsingModelToc(
            [.. document.Pages.OrderBy(p => p.PageNumber)],
            modelToc);

        if (toc is not null)
        {
            document.TableOfContents = toc;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// The overview between the answer tags. A prompt whose answer format never closes the tag gets
    /// replies that end at a second opening tag or at the end of the reply, so the summary up to
    /// there is kept rather than the page left without an overview.
    /// </summary>
    private static string? ReadOverviewAnswer(string response, out bool answerClosed)
    {
        answerClosed = true;
        if (Utils.GetStringFromTag(response, PageOverviewPrompt.ANSWER_TAG) is string answer)
        {
            return answer;
        }

        int openTag = response.IndexOf(PageOverviewPrompt.ANSWER_OPEN_TAG, StringComparison.OrdinalIgnoreCase);
        if (openTag < 0)
        {
            return null;
        }

        answerClosed = false;
        int contentStart = openTag + PageOverviewPrompt.ANSWER_OPEN_TAG.Length;
        int nextOpenTag = response.IndexOf(PageOverviewPrompt.ANSWER_OPEN_TAG, contentStart, StringComparison.OrdinalIgnoreCase);
        int contentEnd = nextOpenTag < 0 ? response.Length : nextOpenTag;

        return response[contentStart..contentEnd];
    }

    private async Task<string?> GenerateTopicsFromOverviewsAsync(
        string overviews,
        string? organizationId,
        TopicsPrompts prompts,
        CancellationToken cancellationToken = default)
    {
        string? systemPrompt = prompts.TopicsAnalysisSystemPrompt;
        string? userPromptTemplate = prompts.TopicsAnalysisUserPrompt;

        int maxRetries = _settings.IniaAi.MaxRetriesForTopicsGeneration;

        string? topicsAnswer = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            ResponseContentSequence responseContent = await _chatConnector.GetResponseAsync(
                $"""
                {overviews}

                {userPromptTemplate}
                """,
                systemPrompt,
                shouldUseAdvancedModel: true,
                model: _tenantModelFamilyResolver.GetModelOverride(organizationId, true),
                family: _tenantModelFamilyResolver.GetModelFamilyForFunction(organizationId, FunctionType.DocumentProcessing),
                cancellationToken: cancellationToken);

            AccumulateTokens(responseContent);

            string? topicsResponse = responseContent.GetPlainText() ?? string.Empty;

            _logger.LogInformation(
                "Topics based on page overviews generation attempt {Attempt}: {Length} characters",
                attempt,
                topicsResponse.Length);

            topicsAnswer = Utils.GetStringFromTag(topicsResponse, "answer");

            if (IsValidTopicsAnswer(topicsAnswer))
            {
                break;
            }

            if (attempt >= maxRetries)
            {
                _logger.LogWarning(
                    "Maximum retries reached ({MaxRetries}) for generating segments. Proceeding with the last response.",
                    maxRetries);
            }
        }

        return topicsAnswer;
    }

    private static bool IsValidTopicsAnswer(string? topicsAnswer)
    {
        return !string.IsNullOrWhiteSpace(topicsAnswer);
    }

    private async Task<string?> GenerateTopicGroupingsAsync(
        string overviews,
        string topics,
        string? organizationId,
        TopicsPrompts prompts,
        CancellationToken cancellationToken = default)
    {
        string? systemPrompt = prompts.TopicGroupingSystemPrompt;
        string? userPromptTemplate = prompts.TopicGroupingUserPrompt;

        string topicGroupingPrompt = string.Format(
            userPromptTemplate
                ?? throw new InvalidOperationException("User prompt template for topic grouping is null."),
            topics);

        int maxRetries = _settings.IniaAi.MaxRetriesForTopicsGeneration;

        string? topicGroupingAnswer = null;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            ResponseContentSequence responseContent = await _chatConnector.GetResponseAsync(
                $"""
                {overviews}

                {topicGroupingPrompt}
                """,
                systemPrompt ?? "",
                shouldUseAdvancedModel: true,
                model: _tenantModelFamilyResolver.GetModelOverride(organizationId, true),
                family: _tenantModelFamilyResolver.GetModelFamilyForFunction(organizationId, FunctionType.DocumentProcessing),
                cancellationToken: cancellationToken);

            AccumulateTokens(responseContent);

            string? topicGroupingResponse = responseContent.GetPlainText() ?? string.Empty;

            _logger.LogInformation(
                    "Topic grouping based on page overviews generation attempt {Attempt}: {Length} characters",
                    attempt,
                topicGroupingResponse.Length);

            topicGroupingAnswer =
            Utils.GetStringFromTag(topicGroupingResponse, "answer");

            if (IsValidTopicGroupingAnswer(topicGroupingAnswer))
            {
                break;
            }

            if (attempt >= maxRetries)
            {
                _logger.LogWarning(
                    "Maximum retries reached ({MaxRetries}) for generating topic groupings. Proceeding with the last response.",
                    maxRetries);
            }
        }

        return topicGroupingAnswer;
    }

    private static bool IsValidTopicGroupingAnswer(string? topicGroupingAnswer)
    {
        if (string.IsNullOrWhiteSpace(topicGroupingAnswer))
        {
            return false;
        }

        string[] lines = topicGroupingAnswer.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

        return lines.Any(line => line.TrimStart().StartsWith(TOPIC_LABEL, StringComparison.OrdinalIgnoreCase))
            && lines.Any(line => line.TrimStart().StartsWith(PAGES_LABEL, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsOverviewErroneous(Page p)
    {
        return !string.IsNullOrWhiteSpace(p.Overview)
            && p.Overview.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOverviewValid(Page p)
    {
        return !string.IsNullOrWhiteSpace(p.Overview)
            && !p.Overview.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasSummarizableContent(Page p)
    {
        return !string.IsNullOrWhiteSpace(p.Text) || p.IsOcrImageOnly;
    }

    private static bool PageRequiresOverview(Page p)
    {
        return (HasSummarizableContent(p)
            && string.IsNullOrWhiteSpace(p.Overview))
            || (!string.IsNullOrWhiteSpace(p.Overview)
            && p.Overview.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.OrdinalIgnoreCase));
    }

    internal const string NO_OVERVIEW_PROMPT_HASH = "none";

    internal static string ComputeOverviewPromptHash(
        string? systemPrompt, string? userPromptTemplate)
    {
        string combined = $"{systemPrompt ?? string.Empty}||{userPromptTemplate ?? string.Empty}";
        return combined.GetSha256Hash();
    }

    #endregion
}

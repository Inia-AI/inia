using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Content;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Core.Documents;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Tools.PdfOcr;
using IniaAi.FileFormats;
using IniaAi.FileFormats.Images;
using IniaAi.FileFormats.Pdf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using TokenExecutionContext = IniaAi.Domain.Entities.TokenUsage.Enums.ExecutionContext;

namespace IniaAi.Core.Services;

/// <summary>
/// Context for recording per-page OCR token usage.
/// </summary>
public record OcrTokenRecordingContext
{
    public required ITokenUsageService TokenUsageService { get; init; }
    public required string OrganizationId { get; init; }
    public required string DocumentId { get; init; }
    public string? UserId { get; init; }
    public string? WorkflowTypeId { get; init; }
    public string? WorkflowExecutionId { get; init; }
}

public class OcrTranscriptionService : ITokenAccumulator
{
    private const int DEFAULT_MAX_OUTPUT_TOKENS = 16384;
    private const string CONTENT_FILTER_MARKER = "content filtering";
    private const int DEFAULT_RENDER_PARALLELISM = 1;

    /// <summary>
    /// Keeps most page renders under the compressor's size target so they skip its second
    /// rasterization; verified quality-neutral against the Aspose default of 100 on a
    /// ground-truth OCR corpus.
    /// </summary>
    private const int RENDER_JPEG_QUALITY = 90;

    /// <summary>
    /// Temporarily disables the fidelity-judge pass: every transcription is accepted as good
    /// and the page image is not sent to the model a second time. Flip to re-enable.
    /// </summary>
    private static readonly bool _fidelityJudgeEnabled = false;
    private readonly ILogger _logger;
    private readonly IChatConnector _chatConnector;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IPdfFormat _pdfFormat;
    private PdfOcrPrompts _prompts = new();
    private readonly OcrSettings? _ocrSettings;
    private readonly int _maxOutputTokens;
    private readonly bool _shouldUseAdvancedModel;
    private readonly string? _modelIdOverride;

    // Token accumulation for tracking (thread-safe for parallel calls)
    private int _accumulatedInputTokens;
    private int _accumulatedOutputTokens;

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

    public class OcrProcessingResult
    {
        public int PageNumber { get; internal set; }
        public string? MarkdownResults { get; internal set; }
        public bool Succeeded { get; internal set; }
        public bool NeedsReview { get; internal set; }
        public bool EmptyTranscription { get; internal set; }

        /// <summary>
        /// The model refused to transcribe the page. The page then has <see cref="NeedsReview"/>
        /// set and no text, so it goes to blocks as an image and the refusal never stands in
        /// for its content.
        /// </summary>
        public ModelRefusal? Refusal { get; internal set; }
        public OcrProcessingResult(int pageNumber)
        {
            PageNumber = pageNumber;
        }
    }

    public OcrTranscriptionService(
        ILogger<OcrTranscriptionService> logger,
        IChatConnector chatConnector,
        IServiceScopeFactory serviceScopeFactory,
        IPdfFormat pdfFormat,
        IOptions<PlatformSettings> platformSettings)
    {
        _logger = logger;
        _chatConnector = chatConnector;
        _serviceScopeFactory = serviceScopeFactory;
        _pdfFormat = pdfFormat;
        _ocrSettings = platformSettings.Value.OcrSettings;
        _maxOutputTokens = _ocrSettings?.OcrMaxOutputTokens is int configured and > 0
            ? configured
            : DEFAULT_MAX_OUTPUT_TOKENS;
        _shouldUseAdvancedModel = _ocrSettings?.ShouldUseAdvancedModel ?? true;
        string? modelIdOverride = _ocrSettings?.ModelIdOverride;
        _modelIdOverride = string.IsNullOrWhiteSpace(modelIdOverride) ? null : modelIdOverride;
    }

    public async Task SetupPromptsAsync()
    {
        _logger.LogInformation("Starting PDF OCR prompts setup");

        try
        {
            using IServiceScope scope = _serviceScopeFactory.CreateScope();
            ICoreUnitOfWork coreUnitOfWork = scope.ServiceProvider.GetRequiredService<ICoreUnitOfWork>();

            _prompts = await coreUnitOfWork.Tools
                    .GetPdfOcrPromptsAsNoTrackingAsync()
                    .ConfigureAwait(false)
                ?? new PdfOcrPrompts();

            _logger.LogInformation("PDF OCR prompts setup completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to setup PDF OCR prompts");
            _prompts = new PdfOcrPrompts();
        }
    }

    /// <summary>
    /// Processes the specified pages of a PDF in parallel: each page is rendered to an image
    /// and transcribed in one shot by the model <see cref="OcrSettings"/> names. When <see cref="_fidelityJudgeEnabled"/>
    /// is set, the transcription is additionally fidelity-checked by the basic-model judge.
    /// </summary>
    /// <param name="pdfBytes">
    /// A <see cref="ReadOnlyMemory{Byte}"/> containing the raw PDF file data.
    /// </param>
    /// <param name="documentName">The name an error about the file shows.</param>
    /// <param name="pageNumbers">
    /// A sequence of 1-based page indices to process. Pages not in this list will be skipped.
    /// Oversized pages render at a reduced per-page DPI so every page stays within the
    /// provider image limit.
    /// </param>
    /// <param name="family">
    /// The model family to use for the transcription and judge calls.
    /// </param>
    /// <param name="tokenRecordingContext">
    /// Optional context for recording per-page token usage.
    /// </param>
    /// <param name="cancellationToken">
    /// A token that can be used to cancel the entire PDF processing operation.
    /// When signaled, no new pages will be queued, and in-flight tasks will observe cancellation.
    /// </param>
    /// <returns>
    /// A <see cref="ConcurrentDictionary{TKey, TValue}"/> mapping each processed page number
    /// to its <see cref="OcrProcessingResult"/>. A page routed by image (content-filter block,
    /// or a bad judge verdict when the judge is enabled) has
    /// <see cref="OcrProcessingResult.NeedsReview"/> set and carries no text; a page whose
    /// transcription call failed has <see cref="OcrProcessingResult.Succeeded"/> false.
    /// The dictionary may contain partial results if some pages errored or were cancelled.
    /// </returns>
    public async Task<ConcurrentDictionary<int, OcrProcessingResult>> ProcessPdf(
        ReadOnlyMemory<byte> pdfBytes,
        string documentName,
        IEnumerable<int> pageNumbers,
        ModelFamily family,
        OcrTokenRecordingContext? tokenRecordingContext = null,
        CancellationToken cancellationToken = default)
    {
        VisionTier visionTier = GetVisionTier();

        MemoryStream docStream = MemoryMarshal.TryGetArray(pdfBytes, out ArraySegment<byte> segment)
            ? new MemoryStream(segment.Array!, segment.Offset, segment.Count, writable: false)
            : new MemoryStream(pdfBytes.ToArray());

        var results = new ConcurrentDictionary<int, OcrProcessingResult>();

        int llmMax = ResolveLlmParallelism(_ocrSettings);

        // Bounded so rendering back-pressures instead of buffering every page image at once.
        var channel = Channel.CreateBounded<(int PageNumber, BinaryContent PageImage)>(
            new BoundedChannelOptions(llmMax * 2) { FullMode = BoundedChannelFullMode.Wait });

        var totalStopwatch = Stopwatch.StartNew();
        var renderStopwatch = Stopwatch.StartNew();

        _logger.LogInformation("Starting LLM transcription phase with MaxDegreeOfParallelism of {maxDegreeOfParallelism}", llmMax);

        using var consumerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task consumer = Parallel.ForEachAsync(
            channel.Reader.ReadAllAsync(consumerCancellation.Token),
            new ParallelOptions { MaxDegreeOfParallelism = llmMax, CancellationToken = consumerCancellation.Token },
            async (item, ct) =>
            {
                try
                {
                    results[item.PageNumber] = await TranscribeAndJudgePageAsync(
                        item.PageNumber,
                        item.PageImage,
                        family,
                        tokenRecordingContext,
                        ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "LLM error for page {Page}", item.PageNumber);
                    results[item.PageNumber] = new OcrProcessingResult(item.PageNumber);
                }
            });

        var renderParallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = ResolveRenderParallelism(_ocrSettings),
            CancellationToken = cancellationToken
        };

        _logger.LogInformation("Starting page render phase with MaxDegreeOfParallelism of {maxDegreeOfParallelism}", renderParallelOptions.MaxDegreeOfParallelism);

        try
        {
            // The document lives only until its last page renders, so the model calls after that hold no copy of the file.
            using IPdfDocument pdf = _pdfFormat.Open(docStream, documentName);
            await Parallel.ForEachAsync(pageNumbers, renderParallelOptions, async (pageNumber, ct) =>
            {
                ct.ThrowIfCancellationRequested();
                var conversionStopwatch = Stopwatch.StartNew();

                PdfBox pageBox = pdf.DisplayedBox(pageNumber);
                int renderDpi = ModelImageLimits.ResolveRenderDpi(pageBox.Width, pageBox.Height, visionTier);

                BinaryContent pageImage = ImageCompressor.CompressJpeg(
                    pdf.RenderJpeg(pageNumber, renderDpi, RENDER_JPEG_QUALITY),
                    targetSize: 3200000);

                conversionStopwatch.Stop();
                _logger.LogInformation(
                    "Converted page {pageNumber} to image in {elapsedMilliseconds} ms",
                    pageNumber,
                    conversionStopwatch.ElapsedMilliseconds);

                await channel.Writer.WriteAsync((pageNumber, pageImage), ct);
            });
        }
        catch
        {
            // Tear the consumer down before propagating, so a failed render doesn't keep
            // paying for model calls on already-buffered pages.
            consumerCancellation.Cancel();
            channel.Writer.Complete();
            try
            {
                await consumer;
            }
            catch (OperationCanceledException)
            {
            }

            throw;
        }

        channel.Writer.Complete();

        renderStopwatch.Stop();

        _logger.LogInformation(
            "Page rendering completed in {elapsedMilliseconds} ms",
            renderStopwatch.ElapsedMilliseconds);

        await consumer;

        totalStopwatch.Stop();

        _logger.LogInformation(
            "Pipeline summary: render phase = {RenderMs} ms, total pipeline duration = {TotalMs} ms",
            renderStopwatch.ElapsedMilliseconds,
            totalStopwatch.ElapsedMilliseconds);

        return results;
    }

    public async Task<OcrProcessingResult> ProcessImageAsync(
        BinaryContent imageData,
        ModelFamily family,
        OcrTokenRecordingContext? tokenRecordingContext = null,
        int pageNumber = 1,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await TranscribeAndJudgePageAsync(
            pageNumber,
            imageData,
            family,
            tokenRecordingContext,
            cancellationToken);
    }

    /// <summary>
    /// Defaults to serial rendering: Aspose serializes page rasterization on the document internally
    /// and rendering outpaces the LLM transcription it feeds, so parallel renders add no throughput
    /// and only stack each page's bitmap churn onto the peak — measured to cut peak render memory
    /// ~45% versus any degree of parallelism of two or more. A positive configured value still wins.
    /// </summary>
    internal static int ResolveRenderParallelism(OcrSettings? settings)
    {
        return int.TryParse(settings?.OcrMaxDegreeOfParallelism, out int configured) && configured > 0
            ? configured
            : DEFAULT_RENDER_PARALLELISM;
    }

    /// <summary>
    /// The transcription calls are network-bound, but each in-flight page also holds its
    /// rendered image in memory, so the default stays at half the processor count.
    /// </summary>
    internal static int ResolveLlmParallelism(OcrSettings? settings)
    {
        return int.TryParse(settings?.LlmMaxDegreeOfParallelism, out int configured) && configured > 0
            ? configured
            : Math.Max(1, Environment.ProcessorCount / 2);
    }

    /// <summary>
    /// The largest image the transcribing model reads as it is: render a page or region for OCR to fit it.
    /// </summary>
    public VisionTier GetVisionTier()
    {
        return _chatConnector.GetVisionTier(_shouldUseAdvancedModel, _modelIdOverride);
    }

    private async Task<OcrProcessingResult> TranscribeAndJudgePageAsync(
        int pageNumber,
        BinaryContent pageImage,
        ModelFamily family,
        OcrTokenRecordingContext? tokenRecordingContext,
        CancellationToken cancellationToken)
    {
        var result = new OcrProcessingResult(pageNumber);
        var totalStopwatch = Stopwatch.StartNew();
        var transcriptionStopwatch = new Stopwatch();
        var judgeStopwatch = new Stopwatch();

        try
        {
            transcriptionStopwatch.Start();
            string transcription;
            ModelRefusal? refusal;
            try
            {
                (transcription, refusal) = await GetTranscriptionResponseAsync(
                    pageNumber,
                    pageImage,
                    family,
                    tokenRecordingContext,
                    cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Transcription failed for page {PageNumber}", pageNumber);
                return result;
            }
            finally
            {
                transcriptionStopwatch.Stop();
            }

            if (refusal is not null)
            {
                _logger.LogWarning(
                    "The model refused to transcribe page {PageNumber} of document {DocumentId}. Category: {RefusalCategory}, organization: {OrganizationId}, workflow type: {WorkflowTypeId}, execution: {WorkflowExecutionId}, model: {ModelId}, message: {MessageId}. The page is passed on as an image with no text.",
                    pageNumber,
                    tokenRecordingContext?.DocumentId,
                    refusal.Category,
                    tokenRecordingContext?.OrganizationId,
                    tokenRecordingContext?.WorkflowTypeId,
                    tokenRecordingContext?.WorkflowExecutionId,
                    refusal.ModelId,
                    refusal.RequestId);
                result.NeedsReview = true;
                result.Refusal = refusal;
                return result;
            }

            if (string.IsNullOrWhiteSpace(transcription))
            {
                _logger.LogWarning("Page {PageNumber}: transcription returned no text.", pageNumber);
                result.EmptyTranscription = true;
                return result;
            }

            if (transcription.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.Ordinal))
            {
                bool contentFiltered = transcription.Contains(CONTENT_FILTER_MARKER, StringComparison.OrdinalIgnoreCase);

                if (contentFiltered)
                {
                    _logger.LogWarning(
                        "Page {PageNumber}: transcription was blocked by the model's content filter; "
                        + "the page will be routed by image instead of text. Response: {Response}",
                        pageNumber,
                        transcription);
                    result.NeedsReview = true;
                }
                else
                {
                    _logger.LogError(
                        "Page {PageNumber}: transcription call returned an error. Response: {Response}",
                        pageNumber,
                        transcription);
                }

                return result;
            }

            if (_fidelityJudgeEnabled)
            {
                judgeStopwatch.Start();
                bool qualityBad = await GetJudgeVerdictAsync(
                    pageNumber,
                    pageImage,
                    transcription,
                    family,
                    tokenRecordingContext,
                    cancellationToken);
                judgeStopwatch.Stop();

                if (qualityBad)
                {
                    result.NeedsReview = true;
                    return result;
                }
            }

            result.Succeeded = true;
            result.MarkdownResults = transcription.Replace("&nbsp;", " ");
            return result;
        }
        finally
        {
            totalStopwatch.Stop();

            _logger.LogInformation(
                "OCR timings (Page {PageNumber}): transcription = {TranscriptionMs} ms, judge = {JudgeMs} ms, Total = {TotalMs} ms",
                pageNumber,
                transcriptionStopwatch.ElapsedMilliseconds,
                judgeStopwatch.ElapsedMilliseconds,
                totalStopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<(string Transcription, ModelRefusal? Refusal)> GetTranscriptionResponseAsync(
        int pageNumber,
        BinaryContent pageImage,
        ModelFamily family,
        OcrTokenRecordingContext? tokenRecordingContext,
        CancellationToken cancellationToken)
    {
        var transcriptionContent = new RequestContentSequence();
        transcriptionContent.Add(_prompts.TranscriptionPrompt
            ?? throw new InvalidOperationException("Transcription prompt is not set"));
        transcriptionContent.Add(pageImage);

        ResponseContentSequence responseContent = await _chatConnector.GetResponseAsync(
            transcriptionContent,
            systemPrompt: _prompts.TranscriptionSystemPrompt,
            maxTokens: _maxOutputTokens,
            shouldUseAdvancedModel: _shouldUseAdvancedModel,
            model: _modelIdOverride,
            family: family,
            cancellationToken: cancellationToken);

        AccumulateTokens(responseContent);

        RecordPerPageTokenUsage(
            tokenRecordingContext,
            responseContent,
            UsageType.LlmOcrTranscription,
            pageNumber);

        _ = LogIfTruncated(responseContent, pageNumber, "Transcription");

        string transcription = responseContent.GetPlainText() ?? string.Empty;

        _logger.LogDebug(
            "PageNumber: {pageNumber} - LLM transcription response: {Answer}",
            pageNumber,
            transcription);

        if (responseContent.Refusal is ModelRefusal refusal)
        {
            return (transcription, refusal);
        }

        if (!WrittenRefusal.IsRefusal(transcription))
        {
            return (transcription, null);
        }

        // The connector logs a provider refusal; a written one is found only here.
        ModelRefusal written = WrittenRefusal.ToRefusal(transcription, responseContent.EndpointModelId);
        ModelRefusalLog.Write(_logger, written, responseContent.EndpointModelId ?? ModelProviders.UNKNOWN, null);

        return (transcription, written);
    }

    private async Task<bool> GetJudgeVerdictAsync(
        int pageNumber,
        BinaryContent pageImage,
        string transcription,
        ModelFamily family,
        OcrTokenRecordingContext? tokenRecordingContext,
        CancellationToken cancellationToken)
    {
        try
        {
            var judgeContent = new RequestContentSequence();
            judgeContent.Add(_prompts.JudgePrompt?.Replace("{TEXT_CONTENT}", transcription)
                ?? throw new InvalidOperationException("Judge prompt is not set"));
            judgeContent.Add(pageImage);

            ResponseContentSequence responseContent = await _chatConnector.GetResponseAsync(
                judgeContent,
                systemPrompt: _prompts.JudgeSystemPrompt,
                maxTokens: _maxOutputTokens,
                shouldUseAdvancedModel: false,
                family: family,
                cancellationToken: cancellationToken);

            AccumulateTokens(responseContent);

            RecordPerPageTokenUsage(
                tokenRecordingContext,
                responseContent,
                UsageType.LlmOcrFidelityJudge,
                pageNumber);

            bool truncated = LogIfTruncated(responseContent, pageNumber, "Fidelity judge");

            string judgeResponse = responseContent.GetPlainText() ?? string.Empty;

            _logger.LogDebug(
                "PageNumber: {pageNumber} - LLM judge response: {Answer}",
                pageNumber,
                judgeResponse);

            if (judgeResponse.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "PageNumber: {pageNumber} - fidelity judge call returned an error; "
                    + "the fidelity check did not run for this page and the transcription is kept as-is. Response: {Response}",
                    pageNumber,
                    judgeResponse);
                return false;
            }

            OcrJudgeVerdict verdict = OcrJudgeResponseParser.Parse(judgeResponse);

            bool verdictTruncatedBeforeCompletion = truncated && !verdict.VerdictPresent;

            if (verdict.QualityBad || verdictTruncatedBeforeCompletion)
            {
                _logger.LogInformation(
                    "PageNumber: {pageNumber} - OCR fidelity {Reason}; the page will be routed by image instead of text.",
                    pageNumber,
                    verdictTruncatedBeforeCompletion ? "verdict truncated before completion" : "judged unreliable");

                return true;
            }

            if (!verdict.VerdictPresent)
            {
                _logger.LogWarning(
                    "PageNumber: {pageNumber} - fidelity judge returned no <ocr_quality> verdict; "
                    + "the fidelity check did not run for this page and the transcription is kept as-is.",
                    pageNumber);
            }

            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "PageNumber: {pageNumber} - fidelity judge failed; "
                + "the fidelity check did not run for this page and the transcription is kept as-is.",
                pageNumber);
            return false;
        }
    }

    private bool LogIfTruncated(ResponseContentSequence response, int pageNumber, string pass)
    {
        if (!response.IsTruncated)
        {
            return false;
        }

        _logger.LogWarning(
            "OCR {Pass} for page {PageNumber} was truncated at the {MaxTokens}-token output cap; "
                + "the stored page text is incomplete. Raise PlatformSettings:OcrSettings:OcrMaxOutputTokens.",
            pass,
            pageNumber,
            _maxOutputTokens);

        return true;
    }

    private static void RecordPerPageTokenUsage(
        OcrTokenRecordingContext? context,
        ResponseContentSequence response,
        UsageType usageType,
        int pageNumber)
    {
        if (context is null)
        {
            return;
        }

        // Extract model ID from response metadata
        string modelId = "ocr";
        if (response.Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? modelValue)
            && modelValue is string modelString
            && !string.IsNullOrWhiteSpace(modelString))
        {
            modelId = modelString;
        }

        string usageDescription = usageType is UsageType.LlmOcrTranscription
            ? $"OCR transcription for page {pageNumber}"
            : $"OCR fidelity judge for page {pageNumber}";

        // Fire and forget - record token usage per page
        _ = context.TokenUsageService.RecordLlmUsageAsync(new LlmUsageRecord
        {
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            WorkflowTypeId = context.WorkflowTypeId,
            WorkflowExecutionId = context.WorkflowExecutionId,
            UsageType = usageType,
            ExecutionContext = context.WorkflowExecutionId is not null
                ? TokenExecutionContext.WorkflowExecution
                : TokenExecutionContext.DocumentUpload,
            Provider = ModelIdNormalizer.GetProvider(modelId),
            Family = ModelIdNormalizer.GetFamily(modelId),
            ModelId = ModelIdNormalizer.Normalize(modelId),
            Components = UsageComponentHelper.FromTokens(response.InputTokens, response.OutputTokens),
            DocumentId = context.DocumentId,
            PageId = pageNumber.ToString(),
            Description = usageDescription,
            IsSingleRequest = response.RequestCount == 1,
            EndpointModelId = response.EndpointModelId,
        });
    }
}

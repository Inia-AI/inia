using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Anthropic;
using IniaAi.AiConnection.AwsBedrock;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common;
using IniaAi.Common.Constants;
using IniaAi.Common.Interfaces.Services;
using IniaAi.Common.Text;
using IniaAi.Core.Common.Helpers;
using IniaAi.Core.Documents;
using IniaAi.Core.Rag;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Chunking;
using IniaAi.Core.Workflows.Interfaces;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.Domain.OutputFormat;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace IniaAi.Core.Workflows.Strategies;

public class AiQueryStrategy : IBlockStrategy
{
    private const int MAX_OUTPUT_FORMAT_ATTEMPTS = 2;


    public async Task<BlockStrategyOutput> ExecuteBlockAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BlockStrategyOutput output = new();

        AiQueryProperties props = block.TryGetProperties<AiQueryProperties>()
            ?? throw new InvalidOperationException($"Block properties must be of type {nameof(AiQueryProperties)}");

        block.ExecutionState ??= new(block);

        bool shouldChunkLargeInputContexts = props.ShouldChunkLargeInputContexts
            && block.RagSettings.Count > 0
            && block.Documents.Count > 0;

        if (!ValidateRagSettingsForInputContext(block, context))
        {
            const string skipReason = "Block uses {{InputContext}} placeholder but no RAG settings are configured. Add RAG settings to specify how to retrieve document content.";
            block.ExecutionState.MarkAsSkipped(skipReason);

            return output;
        }

        if (!ValidateDocumentsForRagAsync(block, context))
        {
            const string skipReason = "No documents found for block";
            block.ExecutionState.MarkAsSkipped(skipReason);

            return output;
        }

        // Reset RAG token accumulator before filtering to track RAG-specific usage
        context.RagService.ResetTokenAccumulator();

        Dictionary<string, Dictionary<int, Page>> filteredPages;
        try
        {
            filteredPages = await ExecuteRagGetFilteredPagesAsync(
                block,
                context,
                cancellationToken).ConfigureAwait(false);
        }
        catch (EmbeddingsUnavailableException unavailable)
        {
            block.ExecutionState.MarkAsSkipped(unavailable.Message);

            return output;
        }

        // Record RAG token usage (fire-and-forget) with selected page metadata
        RecordRagTokenUsage(block, context, filteredPages);

        IEnumerable<Page> contextPages = filteredPages
            .SelectMany(kvp => kvp.Value)
            .Select(kvp => kvp.Value);

        List<Page> pagesNeedingImages = SelectPagesNeedingImages(props, contextPages);

        if (pagesNeedingImages.Count > 0)
        {
            PagesHelper.GenerateImagesFromPdfPages(context.PdfFormat, context.Logger, pagesNeedingImages);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (!ValidateFilteredPages(block, context, filteredPages))
        {
            const string skipReason = "No pages were filtered for the block";
            block.ExecutionState.MarkAsSkipped(skipReason);

            return output;
        }

        if (shouldChunkLargeInputContexts)
        {
            await ExecuteWithChunkingAsync(
                block,
                props,
                context,
                filteredPages,
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await ExecuteWithoutChunkingAsync(
                block,
                props,
                context,
                filteredPages,
                cancellationToken).ConfigureAwait(false);
        }

        context.Logger.LogDebug(
            "{Block} response: {Response}",
            block,
            block.BlockResponse?.RawResponse);

        props.ExecutionState?.UserPrompt = AsksForCitations(props, context)
            ? props.ExecutionState?.UserPrompt + RequestContentSequenceHelper.CITATIONS_APPENDAGE
            : props.ExecutionState?.UserPrompt;

        return output;
    }

    private static bool ValidateRagSettingsForInputContext(Block block, BlockExecutionContext context)
    {
        // If block uses InputContext placeholder but has no RAG settings, it cannot execute properly
        if (block.RagSettings.Count == 0 && block.HasPlaceholder(SharedInputKeys.INPUT_CONTEXT))
        {
            context.Logger.LogWarning(
                "{Block} Block uses {{InputContext}} placeholder but has no RAG settings configured, skipping block",
                block);

            return false;
        }

        return true;
    }

    private static bool ValidateDocumentsForRagAsync(Block block, BlockExecutionContext context)
    {
        if (block.RagSettings.Count == 0 || block.Documents.Count != 0)
        {
            return true;
        }

        context.Logger.LogWarning(
            "{Block} No documents found for block with {RagSettingsCount} data retrieval settings, skipping block",
            block,
            block.RagSettings.Count);

        return false;
    }

    private static bool ValidateFilteredPages(
        Block block,
        BlockExecutionContext context,
        Dictionary<string, Dictionary<int, Page>> filteredPages)
    {
        if (block.RagSettings.Count == 0
            || (filteredPages.Count != 0
            && filteredPages.Any(kvp => kvp.Value.Count != 0)))
        {
            return true;
        }

        context.Logger.LogWarning(
            "{Block} No pages found for block, using {RagSettingsCount} data retrieval settings out of {DocumentCount} documents, skipping block",
            block,
            block.RagSettings.Count,
            block.Documents.Count);

        return false;
    }

    private static async Task ExecuteWithChunkingAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<string, Dictionary<int, Page>> filteredPages,
        CancellationToken cancellationToken = default)
    {
        Dictionary<string, Dictionary<int, Page>> chunks = ImageCapChunker.CapByImageCount(
            ChunkPages(filteredPages, block, props),
            RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST);

        await RunChunkedRequestsAndBuildResponseAsync(
            block,
            props,
            context,
            chunks,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunChunkedRequestsAndBuildResponseAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<string, Dictionary<int, Page>> chunks,
        CancellationToken cancellationToken = default)
    {
        var combinedResponse = new StringBuilder();

        block.ExecutionState ??= new(block);
        props.ExecutionState ??= new(props);

        // Conditional-tag blocks early-exit per chunk (inherently sequential); a single chunk has
        // nothing to parallelise. Everything else fans its chunk LLM calls out concurrently
        // (throttled by the global Bedrock semaphore) and merges in chunk order — byte-identical to
        // the sequential path for the same per-chunk responses.
        bool runSequentially =
            !string.IsNullOrWhiteSpace(props.ExecutionState.ConditionalTag)
            || chunks.Count <= 1;

        if (runSequentially)
        {
            await ExecuteChunksSequentiallyAsync(
                block,
                props,
                context,
                chunks,
                combinedResponse,
                cancellationToken);
        }
        else
        {
            await ExecuteChunksInParallelAsync(
                block,
                props,
                context,
                chunks,
                combinedResponse,
                cancellationToken);
        }

        string? json = GetJsonResponseForFormat(props) ?? ExtractJson(combinedResponse.ToString());

        // Extract debug info from RAG execution state if available
        string? debugInfo = block.RagSettings
            .FirstOrDefault(rs => rs.ExecutionState?.DebugInfo is not null)?
            .ExecutionState?
            .DebugInfo;

        _ = new BlockResponse(block)
        {
            RawResponse = combinedResponse.ToString().Trim(),
            JsonResponse = json,
            HtmlResponse = combinedResponse.ToString().Trim(),
            MarkdownResponse = combinedResponse.ToString().Trim(),
            DebugInfo = debugInfo,
        };
    }

    /// <summary>
    /// Runs the chunks one at a time in order, with the conditional-tag early-exit. Behaviour is
    /// identical to the original chunking loop.
    /// </summary>
    internal static async Task ExecuteChunksSequentiallyAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<string, Dictionary<int, Page>> chunks,
        StringBuilder combinedResponse,
        CancellationToken cancellationToken)
    {
        int iteration = 1;

        foreach ((string chunkName, Dictionary<int, Page> chunk) in chunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Do NOT reassign ExecutionState.UserPrompt here. The LLM call downstream
            // (ExecuteSingleChunkAsync → BuildAndValidateUserPromptContent) reads from
            // props.UserPromptTemplate directly, not from state.UserPrompt, so this reassignment had
            // no effect on the call. But it also clobbered the substituted value that
            // SetExecutionState populated earlier, which left state.UserPrompt holding the raw
            // template with {placeholders} intact on every chunked block — and that broke
            // signature-replay (which string-searches the substituted prompt for the captured old value).

            ResponseContentSequence responseContent = await ExecuteSingleChunkWithFormatRetryAsync(
                block,
                props,
                context,
                chunk,
                cancellationToken);

            string pageNumbers = string.Join(", ", chunk.Keys.OrderBy(k => k));
            string iterationName = $"Iteration {iteration} (Page {pageNumbers})";

            string? response = responseContent.GetPlainText();

            if (response is not null)
            {
                _ = combinedResponse.AppendLine($"\n{iterationName}\n");
                _ = combinedResponse.AppendLine(response);
            }

            props.ExecutionState!.InputContext += BuildChunkInputContext(props, iterationName, chunk);

            if (IsConditionalTagMet(props.ExecutionState.ConditionalTag, response))
            {
                context.Logger.LogDebug(
                    "{Block} Conditional tag met after {Iteration} iterations, stopping further processing",
                    block,
                    iteration);

                break;
            }

            iteration++;
        }
    }

    /// <summary>
    /// Runs every chunk's LLM call concurrently (throttled by the global Bedrock semaphore in
    /// <c>BaseChatConnector</c>), then merges the results into <paramref name="combinedResponse"/>,
    /// <c>props.ExecutionState.InputContext</c>, <c>context.BlockAnnotations</c> and the block props
    /// strictly in chunk order — so the output equals the sequential path for the same responses.
    /// Each chunk task is side-effect-free (writes only its own annotations buffer); one chunk
    /// throwing fails the block via <see cref="Task.WhenAll"/>.
    /// </summary>
    internal static async Task ExecuteChunksInParallelAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<string, Dictionary<int, Page>> chunks,
        StringBuilder combinedResponse,
        CancellationToken cancellationToken)
    {
        props.OutputFormatAttemptCount = 0;
        props.OutputFormatLastError = null;
        props.OutputFormatFailedAttemptResponse = null;
        props.ExtractedValues = null;

        // Linked CTS gives fail-fast semantics: the first chunk failure cancels its siblings,
        // mirroring the sequential path where later chunks never ran after a failure.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        Task<(int Index, Dictionary<int, Page> Chunk, ChunkExecution Execution, BlockAnnotations Annotations)>[] tasks =
            [.. chunks.Values.Select(async (chunk, index) =>
            {
                BlockAnnotations chunkAnnotations = new();

                try
                {
                    ChunkExecution execution = await RunChunkAsync(
                        block,
                        props,
                        context,
                        chunk,
                        chunkAnnotations,
                        linkedCts.Token).ConfigureAwait(false);

                    return (index, chunk, execution, chunkAnnotations);
                }
                catch
                {
                    linkedCts.Cancel();
                    throw;
                }
            })];

        (int Index, Dictionary<int, Page> Chunk, ChunkExecution Execution, BlockAnnotations Annotations)[] results;

        try
        {
            results = await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The awaited WhenAll rethrows the first real chunk fault (cancelled siblings end
            // Canceled, not Faulted, so they cannot win); parent cancellation arrives here as OCE.
            if (ex is OutputFormatExhaustedException exhausted)
            {
                ApplyExhaustionToProps(props, exhausted);
            }

            throw;
        }

        int iteration = 1;

        foreach ((int _, Dictionary<int, Page> chunk, ChunkExecution execution, BlockAnnotations annotations)
            in results.OrderBy(r => r.Index))
        {
            string pageNumbers = string.Join(", ", chunk.Keys.OrderBy(k => k));
            string iterationName = $"Iteration {iteration} (Page {pageNumbers})";

            string? response = execution.Response.GetPlainText();

            if (response is not null)
            {
                _ = combinedResponse.AppendLine($"\n{iterationName}\n");
                _ = combinedResponse.AppendLine(response);
            }

            props.ExecutionState!.InputContext += BuildChunkInputContext(props, iterationName, chunk);

            context.BlockAnnotations.AddFacts(annotations.Facts);
            context.BlockAnnotations.AddConfidenceScores(annotations.ConfidenceScores);
            context.BlockAnnotations.AddSources(annotations.Sources);

            ApplyChunkExecutionToProps(props, execution);
            iteration++;
        }
    }

    /// <summary>
    /// Builds the per-chunk debug input-context block appended to
    /// <c>props.ExecutionState.InputContext</c>. Shared by the sequential and parallel paths so the
    /// rendered text is identical.
    /// </summary>
    private static string BuildChunkInputContext(
        AiQueryProperties props,
        string iterationName,
        Dictionary<int, Page> chunk)
    {
        return $$"""
        <{{iterationName}}>
        {{GetDebugInputContext(props, chunk)}}
        </{{iterationName}}>


        """;
    }

    private static bool IsConditionalTagMet(
        string? conditionalTag,
        string? response)
    {
        if (string.IsNullOrWhiteSpace(conditionalTag)
            || string.IsNullOrWhiteSpace(response))
        {
            return false;
        }

        string? tagValue = Utils.GetStringFromTag(response, conditionalTag, processMultipleTags: true);

        return !string.IsNullOrWhiteSpace(tagValue) && !Utils.IsNaValue(tagValue);
    }

    private static string? ExtractJson(string str)
    {
        int firstBraceIndex = str.IndexOf('{');
        int lastBraceIndex = str.LastIndexOf('}');

        if (firstBraceIndex != -1 && lastBraceIndex != -1 && lastBraceIndex > firstBraceIndex)
        {
            string json = str[firstBraceIndex..(lastBraceIndex + 1)];
            return json.IsValidJson() == true
                ? json
                : null;
        }

        return null;
    }

    private static async Task ExecuteWithoutChunkingAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<string, Dictionary<int, Page>> filteredPages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var flattenedFilteredPages = filteredPages
            .SelectMany(kvp => kvp.Value)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.First().Value);

        int imageCount = flattenedFilteredPages.Values.Count(page => page.Image is not null);

        if (imageCount > RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST)
        {
            context.Logger.LogWarning(
                "{block} Input context carries {imageCount} page images, above the per-request limit of {maxImages}. " +
                "Images beyond the limit are not attached (OCR-flagged pages keep theirs first); enable chunking to send every page image.",
                block,
                imageCount,
                RequestContentSequenceHelper.MAX_IMAGES_PER_REQUEST);
        }

        ResponseContentSequence responseContent = await ExecuteSingleChunkWithFormatRetryAsync(
            block,
            props,
            context,
            flattenedFilteredPages,
            cancellationToken);

        block.ExecutionState ??= new(block);
        props.ExecutionState ??= new(props);
        props.ExecutionState.InputContext =
        $$"""
        {{GetDebugInputContext(props, flattenedFilteredPages)}}
        """;

        string? response = responseContent.GetPlainText();

        if (response is null)
        {
            return;
        }

        string? json = GetJsonResponseForFormat(props) ?? ExtractJson(response.ToString());

        // Extract debug info from RAG execution state if available
        string? debugInfo = block.RagSettings
            .FirstOrDefault(rs => rs.ExecutionState?.DebugInfo is not null)
            ?.ExecutionState?.DebugInfo;

        _ = new BlockResponse(block)
        {
            RawResponse = response.ToString().Trim(),
            JsonResponse = json,
            HtmlResponse = response.ToString().Trim(),
            MarkdownResponse = response.ToString().Trim(),
            DebugInfo = debugInfo,
        };
    }

    internal static string GetDebugInputContext(
        AiQueryProperties props,
        Dictionary<int, Page> pages)
    {
        return props.ShouldUseOverviews
            ? PagesHelper.GetOverviewsInputContext(pages)
            : PagesHelper.GetTextsInputContext(pages, useReadableText: true);
    }

    /// <summary>
    /// Sequential wrapper around <see cref="RunChunkAsync"/>: runs one chunk against
    /// <c>context.BlockAnnotations</c> and folds the format-retry state onto the shared block props.
    /// Behaviour is identical to the original single-call path.
    /// </summary>
    private static async Task<ResponseContentSequence> ExecuteSingleChunkWithFormatRetryAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<int, Page> pages,
        CancellationToken cancellationToken = default)
    {
        // Reset runtime captures so a previous run with a format doesn't leave stale
        // OutputFormatAttemptCount/LastError on the block.
        props.OutputFormatAttemptCount = 0;
        props.OutputFormatLastError = null;
        props.OutputFormatFailedAttemptResponse = null;
        props.ExtractedValues = null;

        try
        {
            ChunkExecution execution = await RunChunkAsync(
                block,
                props,
                context,
                pages,
                context.BlockAnnotations,
                cancellationToken).ConfigureAwait(false);

            ApplyChunkExecutionToProps(props, execution);
            return execution.Response;
        }
        catch (OutputFormatExhaustedException ex)
        {
            ApplyExhaustionToProps(props, ex);
            throw;
        }
    }

    private static void ApplyExhaustionToProps(AiQueryProperties props, OutputFormatExhaustedException ex)
    {
        props.OutputFormatAttemptCount = ex.AttemptCount;
        props.OutputFormatLastError = ex.LastError;
        props.OutputFormatFailedAttemptResponse = ex.FailedResponse;
    }

    /// <summary>
    /// Folds a <see cref="ChunkExecution"/>'s format-retry captures onto the shared block props.
    /// Called once per chunk (sequential) or per chunk in merge order (parallel); last write wins,
    /// matching the original per-chunk reset semantics.
    /// </summary>
    private static void ApplyChunkExecutionToProps(AiQueryProperties props, ChunkExecution execution)
    {
        props.OutputFormatAttemptCount = execution.AttemptCount;
        props.OutputFormatLastError = execution.LastError;
        props.OutputFormatFailedAttemptResponse = execution.FailedResponse;
        props.ExtractedValues = null;
        if (execution.ExtractedValues is not null)
        {
            MergeExtractedValues(props, execution.ExtractedValues);
        }
    }

    /// <summary>
    /// Side-effect-free per-chunk execution: the LLM call plus the output-format retry loop, using
    /// LOCAL format state and writing annotations only into <paramref name="annotationsTarget"/> —
    /// it never mutates shared <c>props</c> or <c>context.BlockAnnotations</c>, so chunks can run
    /// concurrently. The caller applies the returned state (props via
    /// <see cref="ApplyChunkExecutionToProps"/>, annotations by merge) in chunk order. On format
    /// exhaustion it throws so the engine marks the block Failed.
    /// </summary>
    private static async Task<ChunkExecution> RunChunkAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<int, Page> pages,
        BlockAnnotations annotationsTarget,
        CancellationToken cancellationToken = default)
    {
        if (props.OutputFormatType == OutputFormatType.None)
        {
            ResponseContentSequence passthrough = await ExecuteSingleChunkAsync(
                block,
                props,
                context,
                pages,
                annotationsTarget,
                cancellationToken).ConfigureAwait(false);

            return new ChunkExecution(passthrough, null, 0, null, null);
        }

        string? lastError = null;
        string? lastFailedResponse = null;

        for (int attempt = 1; attempt <= MAX_OUTPUT_FORMAT_ATTEMPTS; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ResponseContentSequence response = await ExecuteSingleChunkAsync(
                block,
                props,
                context,
                pages,
                annotationsTarget,
                cancellationToken).ConfigureAwait(false);

            string? raw = response.GetPlainText();
            if (string.IsNullOrEmpty(raw))
            {
                lastError = "Model returned an empty response.";
                lastFailedResponse = raw ?? string.Empty;
                continue;
            }

            string cleaned = LenientCleanup.Clean(raw);
            ValidationResult result = OutputFormatValidator.Validate(cleaned, props);
            if (result.IsValid)
            {
                Dictionary<string, string> extracted = OutputFormatExtractor.Extract(cleaned, props);
                return new ChunkExecution(response, extracted, attempt, lastError, lastFailedResponse);
            }

            lastError = result.Error;
            lastFailedResponse = raw;
            context.Logger.LogWarning(
                "{Block} Output format validation failed on attempt {Attempt}/{Max}: {Error}",
                block,
                attempt,
                MAX_OUTPUT_FORMAT_ATTEMPTS,
                lastError);
        }

        throw new OutputFormatExhaustedException(MAX_OUTPUT_FORMAT_ATTEMPTS, lastError, lastFailedResponse);
    }

    /// <summary>One chunk's LLM execution: the response plus the format-retry captures the caller
    /// folds onto the shared block props.</summary>
    private sealed record ChunkExecution(
        ResponseContentSequence Response,
        Dictionary<string, string>? ExtractedValues,
        int AttemptCount,
        string? LastError,
        string? FailedResponse);

    /// <summary>Format-retry exhaustion, carrying the captures the UI renders from block props.</summary>
    internal sealed class OutputFormatExhaustedException : InvalidOperationException
    {
        public int AttemptCount { get; }
        public string? LastError { get; }
        public string? FailedResponse { get; }

        public OutputFormatExhaustedException(int attemptCount, string? lastError, string? failedResponse)
            : base($"Output format validation failed after {attemptCount} attempts: {lastError}")
        {
            AttemptCount = attemptCount;
            LastError = lastError;
            FailedResponse = failedResponse;
        }
    }

    internal static void MergeExtractedValues(
        AiQueryProperties props,
        Dictionary<string, string> incoming)
    {
        // Last-write-wins, so in chunked mode the final chunk's tags override earlier ones.
        if (props.ExtractedValues is null)
        {
            props.ExtractedValues = incoming;
            return;
        }

        foreach (KeyValuePair<string, string> kvp in incoming)
        {
            props.ExtractedValues[kvp.Key] = kvp.Value;
        }
    }

    /// <summary>
    /// Returns the JSON string that should land on <see cref="BlockResponse.JsonResponse"/>
    /// for Json / JsonInTag formats based on the validated <see cref="AiQueryProperties.ExtractedValues"/>.
    /// Returns null for tag-only formats or when nothing has been extracted yet.
    /// </summary>
    internal static string? GetJsonResponseForFormat(AiQueryProperties props)
    {
        if (props.ExtractedValues is not Dictionary<string, string> extracted)
        {
            return null;
        }

        string? key = props.OutputFormatType switch
        {
            OutputFormatType.Json => "Json",
            OutputFormatType.JsonInTag => props.OutputFormatTagName,
            _ => null,
        };

        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        return extracted.TryGetValue(key, out string? value) ? value : null;
    }

    private static async Task<ResponseContentSequence> ExecuteSingleChunkAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<int, Page> pages,
        BlockAnnotations annotationsTarget,
        CancellationToken cancellationToken = default)
    {
        ResponseContentSequence? responseContent = await GenerateResponseBasedOnConnectorTypeAsync(
            block,
            props,
            context,
            pages,
            cancellationToken).ConfigureAwait(false);

        context.Logger.LogDebug(
            "{Block} User Prompt: {UserPrompt}",
            block,
            props.ExecutionState?.UserPrompt);

        if (responseContent is null)
        {
            context.Logger.LogError("{Block} No response for block", block);
            throw new InvalidOperationException("Null response from LLM");
        }

        RecordModelCalled(props, responseContent);

        // Extract model ID from response metadata (actual model used), normalize for consistent tracking
        string modelId = GetModelIdFromResponse(responseContent, props);

        // A refused request is billed like any other, so its usage is recorded before the block fails.
        if (FindRefusal(responseContent) is ModelRefusal refusal)
        {
            // The connector logs a provider refusal; a written one is found only here.
            if (responseContent.Refusal is null)
            {
                ModelRefusalLog.Write(context.Logger, refusal, responseContent.EndpointModelId ?? modelId, null);
            }

            RecordTokenUsage(block, context, responseContent, modelId);
            throw new ModelRefusalException(refusal);
        }

        if (responseContent.Any(c => c.TextContent?.StartsWith(IChatConnector.ERROR_PREFIX, StringComparison.OrdinalIgnoreCase) == true))
        {
            string errorResponse = responseContent.GetPlainText() ?? string.Empty;

            context.Logger.LogError(
                "{Block} Error response from chat connector: {Response}",
                block,
                errorResponse);
            throw new InvalidOperationException("Error response from LLM: " + errorResponse);
        }

        // Record token usage (fire-and-forget)
        RecordTokenUsage(block, context, responseContent, modelId);

        DeAnonymizeResponseSequence(
            context.AnonymisationMap,
            responseContent);

        StripCiteMarks(responseContent);

        if (!context.IsCitationSource)
        {
            return responseContent;
        }

        ProcessAnnotations(
            responseContent,
            pages,
            annotationsTarget);

        return responseContent;
    }

    /// <summary>
    /// The refusal a response carries: the provider's, or one the model wrote as its whole answer.
    /// </summary>
    internal static ModelRefusal? FindRefusal(ResponseContentSequence response)
    {
        if (response.Refusal is ModelRefusal refusal)
        {
            return refusal;
        }

        string? answer = response.GetPlainText();

        return answer is not null && WrittenRefusal.IsRefusal(answer)
            ? WrittenRefusal.ToRefusal(answer, response.EndpointModelId)
            : null;
    }

    private static async Task<ResponseContentSequence?> GenerateResponseBasedOnConnectorTypeAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<int, Page> pages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool shouldUseCaching = props.ShouldUseCachingForInputContext
            && SupportsCaching(context.ChatConnector);

        return shouldUseCaching
            ? await GenerateChatResponseWithCachedInputContextAsync(
                block,
                props,
                context,
                pages,
                cancellationToken).ConfigureAwait(false)
            : await GenerateChatResponseAsync(
                block,
                props,
                context,
                pages,
                cancellationToken).ConfigureAwait(false);
    }

    private static Dictionary<string, Dictionary<int, Page>> ChunkPages(
        Dictionary<string, Dictionary<int, Page>> filteredPages,
        Block block,
        AiQueryProperties props)
    {
        if (block.RagSettings.Any(r => r.Type is RagTypes.UseTableOfContents))
        {
            return filteredPages;
        }

        List<(int Index, Page Page)> allPages = [.. filteredPages
            .SelectMany(kvp => kvp.Value)
            .OrderBy(kvp => kvp.Key)
            .Select(kvp => (kvp.Key, kvp.Value))];

        IPageChunker chunker = ChunkerFactory.Create(props);
        return chunker.ChunkPages(allPages);
    }

    private static void ProcessAnnotations(
        ResponseContentSequence responseContent,
        Dictionary<int, Page> filteredPages,
        BlockAnnotations blockAnnotations)
    {
        BlockAnnotationProcessor annotationProcessor = new(
            new FactExtractor(),
            new ConfidenceScoreExtractor(),
            new SourceExtractor());

        annotationProcessor.ProcessFacts(
            responseContent,
            filteredPages,
            blockAnnotations);
    }

    /// <summary>
    /// A block whose output reaches no report paragraph and no data output asks for no citations: they would go
    /// nowhere, and the model would spend output tokens on them.
    /// </summary>
    private static bool AsksForCitations(AiQueryProperties props, BlockExecutionContext context)
    {
        return props.ShouldGetCitationsFromModel && context.IsCitationSource;
    }

    private static async Task<ResponseContentSequence?> GenerateChatResponseWithCachedInputContextAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<int, Page> pages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        context.Logger.LogDebug(
            "{Block} Sending input context to chat connector with caching",
            block);

        props.ExecutionState ??= new(props);
        AiQueryPropertiesExecutionState propsExecutionState = props.ExecutionState;

        context.Logger.LogDebug(
            "{Block} Filtered pages: {PageCount}",
            block,
            pages.Count);

        RequestContentSequence? userPromptContent = BuildAndValidateUserPromptContent(
            props.UserPromptTemplate,
            block,
            props,
            context);

        if (userPromptContent is null)
        {
            return null;
        }

        RequestContentSequence cachedContent = RequestContentSequenceHelper.BuildContentSequence(
            userPrompt: null, // User prompt goes into user prompt content
            pages,
            props.ShouldUsePageImages,
            props.ShouldUseOverviews,
            AsksForCitations(props, context)) ?? [];

        if (!string.IsNullOrWhiteSpace(props.TextToCache))
        {
            context.Logger.LogDebug(
                "{Block} Adding cached content from text input",
                block);

            RequestContentSequence textInputContent = BuildAndValidateUserPromptContent(
                props.TextToCache,
                block,
                props,
                context) ?? [];

            cachedContent = [.. cachedContent, .. textInputContent];
        }

        if (!cachedContent.Any())
        {
            context.Logger.LogWarning(
                "{Block} No cached content sequence available",
                block);

            return null;
        }

        AnonymiseContentSequence(context.Logger, context.AnonymisationMap, userPromptContent);
        AnonymiseContentSequence(context.Logger, context.AnonymisationMap, cachedContent);

        // TODO: temporary workaround to combine cached and user prompt content sequences
        RequestContentSequence requestContent = [.. cachedContent, .. userPromptContent];

        ResponseContentSequence response;

        string? modelOverride = props.ModelIdOverride
            ?? context.ModelFamilyResolver.GetModelOverride(context.OrganizationId, props.ShouldUseAdvancedModel);

        if (props.ShouldUseThinkingMode)
        {
            try
            {
                response = await context.ChatConnector.GetThinkingResponseWithCachingAsync(
                    userPromptContent,
                    cachedContent,
                    propsExecutionState.SystemPrompt,
                    props.MaxTokens,
                    props.ThinkingBudget,
                    modelOverride,
                    effort: props.Effort,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                response.Request = requestContent;

                return response;
            }
            catch (NotSupportedException)
            {
                context.Logger.LogWarning(
                    "{Block} Thinking mode with caching not supported by {ConnectorType}, falling back to regular caching mode",
                    block,
                    context.ChatConnector.GetType().Name);
            }
        }

        response = await context.ChatConnector.GetResponseWithCachingAsync(
            userPromptContent,
            cachedContent,
            propsExecutionState.SystemPrompt,
            props.MaxTokens,
            props.ShouldUseAdvancedModel,
            modelOverride,
            family: context.ModelFamilyResolver.GetModelFamilyForFunction(context.OrganizationId, FunctionType.WorkflowReasoning),
            effort: props.Effort,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        response.Request = requestContent;

        return response;
    }

    private static async Task<ResponseContentSequence?> GenerateChatResponseAsync(
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context,
        Dictionary<int, Page> pages,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        context.Logger.LogDebug(
            "{Block} Sending input context to chat connector",
            block);

        props.ExecutionState ??= new(props);
        AiQueryPropertiesExecutionState propsExecutionState = props.ExecutionState;

        context.Logger.LogDebug(
            "{Block} Filtered pages: {PageCount}",
            block,
            pages.Count);

        RequestContentSequence? userPromptContent = BuildAndValidateUserPromptContent(
            props.UserPromptTemplate,
            block,
            props,
            context);

        if (userPromptContent is null)
        {
            return null;
        }

        RequestContentSequence? requestContent = RequestContentSequenceHelper.BuildContentSequence(
            userPromptContent,
            pages,
            props.ShouldUsePageImages,
            props.ShouldUseOverviews,
            AsksForCitations(props, context));

        if (requestContent is null)
        {
            context.Logger.LogWarning(
                "{Block} No content sequence available",
                block);

            return null;
        }

        AnonymiseContentSequence(context.Logger, context.AnonymisationMap, requestContent);

        ResponseContentSequence response;

        string? modelOverride = props.ModelIdOverride
            ?? context.ModelFamilyResolver.GetModelOverride(context.OrganizationId, props.ShouldUseAdvancedModel);

        if (props.ShouldUseThinkingMode)
        {
            try
            {
                response = await context.ChatConnector.GetThinkingResponseAsync(
                    requestContent,
                    propsExecutionState.SystemPrompt,
                    props.MaxTokens,
                    props.ThinkingBudget,
                    props.ShouldUseAdvancedModel,
                    modelOverride,
                    effort: props.Effort,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                response.Request = requestContent;

                return response;
            }
            catch (NotSupportedException)
            {
                context.Logger.LogWarning(
                    "{Block} Thinking mode not supported by {ConnectorType}, falling back to regular mode",
                    block,
                    context.ChatConnector.GetType().Name);
            }
        }

        response = await context.ChatConnector.GetResponseAsync(
            requestContent,
            propsExecutionState.SystemPrompt,
            props.MaxTokens,
            props.ShouldUseAdvancedModel,
            modelOverride,
            family: context.ModelFamilyResolver.GetModelFamilyForFunction(context.OrganizationId, FunctionType.WorkflowReasoning),
            effort: props.Effort,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        response.Request = requestContent;

        return response;
    }

    private static async Task<Dictionary<string, Dictionary<int, Page>>> ExecuteRagGetFilteredPagesAsync(
        Block block,
        BlockExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await context.RagService.GetFilteredPagesAsync(
            block,
            context.WorkflowTypeId,
            context.RagResultsCache,
            context.AllPages,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static void DeAnonymizeResponseSequence(
        IReadOnlyDictionary<string, string> anonymisationMap,
        ResponseContentSequence responseContent)
    {
        foreach ((string original, string replacement) in anonymisationMap)
        {
            responseContent.Replace(replacement, original);
        }
    }

    /// <summary>
    /// Before the output format is checked, so a JSON answer the model marked up is judged without
    /// the marks, and neither the report nor a data output receives them.
    /// </summary>
    internal static void StripCiteMarks(ResponseContentSequence responseContent)
    {
        foreach (ResponseCommonContent content in responseContent)
        {
            content.TextContent = CiteTags.Strip(content.TextContent);
        }
    }

    private static void AnonymiseContentSequence(
        ILogger logger,
        IReadOnlyDictionary<string, string> anonymisationMap,
        RequestContentSequence contentSequence)
    {
        _ = logger;

        foreach ((string original, string replacement) in anonymisationMap)
        {
            contentSequence.Replace(original, replacement);
        }
    }

    /// <summary>
    /// The pages that need an image rendered for this block: every page when the block includes page images,
    /// otherwise only the pages whose OCR text was rejected, since their image is their only content.
    /// </summary>
    internal static List<Page> SelectPagesNeedingImages(AiQueryProperties props, IEnumerable<Page> pages)
    {
        return props.ShouldUsePageImages
            ? [.. pages]
            : [.. pages.Where(page => page.IsOcrImageOnly)];
    }

    /// <summary>
    /// Determines if the chat connector supports caching functionality.
    /// </summary>
    /// <param name="connector">The chat connector to check.</param>
    /// <returns>True if the connector supports caching, false otherwise.</returns>
    private static bool SupportsCaching(IChatConnector connector)
    {
        // Anthropic connectors, and unified AWS Bedrock provider support caching
        return connector is AnthropicChatConnector
            or AwsBedrockChatProvider;
    }

    private static RequestContentSequence? BuildAndValidateUserPromptContent(
        string? templateText,
        Block block,
        AiQueryProperties props,
        BlockExecutionContext context)
    {
        RequestContentSequence? userPromptContent = RequestContentSequenceHelper.BuildUserPromptContentSequence(
            templateText,
            context.TextInputs,
            context.ReferencedBlocks,
            AsksForCitations(props, context));

        if (userPromptContent is null)
        {
            context.Logger.LogWarning(
                "{Block} No user prompt content sequence available",
                block);
        }

        return userPromptContent;
    }

    /// <summary>
    /// Records token usage from an LLM response using fire-and-forget pattern.
    /// </summary>
    private static void RecordTokenUsage(
        Block block,
        BlockExecutionContext context,
        ResponseContentSequence response,
        string modelId)
    {
        if (string.IsNullOrEmpty(context.OrganizationId))
        {
            context.Logger.LogWarning(
                "{Block} Cannot record token usage - OrganizationId is null",
                block);
            return;
        }

        // Extract cache tokens from metadata (keys match Anthropic API response format)
        int cacheCreationTokens = response.Metadata.TryGetValue(
            "CacheCreationInputTokens", out object? cacheCreation)
            ? Convert.ToInt32(cacheCreation)
            : 0;

        int cacheReadTokens = response.Metadata.TryGetValue(
            "CacheReadInputTokens", out object? cacheRead)
            ? Convert.ToInt32(cacheRead)
            : 0;

        var record = new LlmUsageRecord
        {
            OrganizationId = context.OrganizationId,
            UserId = context.UserId,
            WorkflowTypeId = context.WorkflowTypeId,
            WorkflowExecutionId = context.WorkflowExecutionId,
            SectionId = context.SectionId,
            BlockId = block.Id,
            UsageType = UsageType.LlmBlockExecution,
            ExecutionContext = context.ExecutionContext,
            BlockType = block.Type,
            Provider = GetProviderFromConnector(context.ChatConnector),
            Family = ModelIdNormalizer.GetFamily(modelId),
            ModelId = modelId,
            Components = UsageComponentHelper.FromTokens(
                response.InputTokens,
                response.OutputTokens,
                cacheCreationTokens,
                cacheReadTokens),
            Description = $"AI Query block '{block.Name}'",
            IsSingleRequest = response.RequestCount == 1,
            EndpointModelId = response.EndpointModelId,
        };

        // Fire-and-forget - don't await to avoid blocking workflow execution
        _ = context.TokenUsageService.RecordLlmUsageAsync(record, CancellationToken.None);
    }

    private static string GetProviderFromConnector(IChatConnector connector)
    {
        return connector switch
        {
            AnthropicChatConnector => "anthropic-api",
            AwsBedrockChatProvider => "aws-bedrock",
            _ => "unknown"
        };
    }

    /// <summary>
    /// Extracts and normalizes the model ID from the response metadata.
    /// Falls back to configured overrides or standard/advanced indicators if not available.
    /// </summary>
    private static void RecordModelCalled(AiQueryProperties props, ResponseContentSequence response)
    {
        if (props.ExecutionState is { } executionState
            && response.Metadata.TryGetValue(ModelIdNormalizer.ENDPOINT_MODEL_METADATA_KEY, out object? endpoint)
            && endpoint is string { Length: > 0 } endpointModelId)
        {
            executionState.ModelCalled = endpointModelId;
        }
    }

    private static string GetModelIdFromResponse(
        ResponseContentSequence response,
        AiQueryProperties props)
    {
        // First try to get the actual model from the API response
        if (response.Metadata.TryGetValue(ModelIdNormalizer.MODEL_METADATA_KEY, out object? modelValue)
            && modelValue is string modelString
            && !string.IsNullOrWhiteSpace(modelString))
        {
            return ModelIdNormalizer.Normalize(modelString);
        }

        // Fall back to configured override or standard/advanced placeholder
        string fallbackId = props.ModelIdOverride
            ?? (props.ShouldUseAdvancedModel ? "advanced" : "standard");

        return ModelIdNormalizer.Normalize(fallbackId);
    }

    /// <summary>
    /// Records RAG token usage from page filtering operations (LLM filtering and query embeddings).
    /// Includes metadata about which pages were selected and per-call LLM details.
    /// </summary>
    private static void RecordRagTokenUsage(
        Block block,
        BlockExecutionContext context,
        Dictionary<string, Dictionary<int, Page>> filteredPages)
    {
        if (string.IsNullOrEmpty(context.OrganizationId))
        {
            return;
        }

        // Build metadata with selected page information and per-call LLM tracking
        string? metadata = BuildRagMetadata(block, filteredPages, context.RagService.LastLlmCalls);

        // Record LLM token usage for RAG filtering
        int inputTokens = context.RagService.AccumulatedInputTokens;
        int outputTokens = context.RagService.AccumulatedOutputTokens;

        if (inputTokens > 0 || outputTokens > 0)
        {
            string llmModelId = context.RagService.LastUsedModelId ?? "rag-filtering";
            var llmRecord = new LlmUsageRecord
            {
                OrganizationId = context.OrganizationId,
                UserId = context.UserId,
                WorkflowTypeId = context.WorkflowTypeId,
                WorkflowExecutionId = context.WorkflowExecutionId,
                SectionId = context.SectionId,
                BlockId = block.Id,
                UsageType = UsageType.LlmRagFiltering,
                ExecutionContext = context.ExecutionContext,
                BlockType = block.Type,
                Provider = ModelIdNormalizer.GetProvider(llmModelId),
                Family = ModelIdNormalizer.GetFamily(llmModelId),
                ModelId = ModelIdNormalizer.Normalize(llmModelId),
                Components = UsageComponentHelper.FromTokens(inputTokens, outputTokens),
                Description = $"RAG page filtering for block '{block.Name}'",
                Metadata = metadata
            };

            _ = context.TokenUsageService.RecordLlmUsageAsync(llmRecord, CancellationToken.None);
        }

        // Record embedding token usage for RAG query embeddings
        int embeddingTokens = context.RagService.AccumulatedEmbeddingTokens;

        if (embeddingTokens > 0)
        {
            string embeddingModelId = context.RagService.LastUsedEmbeddingModelId ?? "embed-english-v3.0";
            var embeddingRecord = new EmbeddingUsageRecord
            {
                OrganizationId = context.OrganizationId,
                UserId = context.UserId,
                WorkflowTypeId = context.WorkflowTypeId,
                WorkflowExecutionId = context.WorkflowExecutionId,
                SectionId = context.SectionId,
                BlockId = block.Id,
                UsageType = UsageType.EmbeddingQuery,
                ExecutionContext = context.ExecutionContext,
                BlockType = block.Type,
                Provider = ModelIdNormalizer.GetProvider(embeddingModelId),
                Family = ModelIdNormalizer.GetFamily(embeddingModelId),
                ModelId = embeddingModelId,
                Components = UsageComponentHelper.ForEmbedding(embeddingTokens),
                Description = $"RAG embedding query for block '{block.Name}'",
                Metadata = metadata
            };

            _ = context.TokenUsageService.RecordEmbeddingUsageAsync(embeddingRecord, CancellationToken.None);
        }
    }

    /// <summary>
    /// Builds JSON metadata with RAG filtering results (selected pages per document) and per-call LLM details.
    /// </summary>
    private static string? BuildRagMetadata(
        Block block,
        Dictionary<string, Dictionary<int, Page>> filteredPages,
        IReadOnlyCollection<RagLlmCall>? llmCalls)
    {
        if (filteredPages.Count == 0 && (llmCalls is null || llmCalls.Count == 0))
        {
            return null;
        }

        // Get RAG type from block settings
        RagTypes? ragType = block.RagSettings.FirstOrDefault()?.Type;

        var metadata = new
        {
            ragType = ragType?.ToString(),
            totalPagesSelected = filteredPages.Sum(d => d.Value.Count),
            documents = filteredPages.Select(kvp => new
            {
                documentId = kvp.Key,
                pageNumbers = kvp.Value.Keys.OrderBy(p => p).ToList()
            }).ToList(),
            llmCalls = llmCalls?.Select(c => new
            {
                callType = c.CallType,
                inputTokens = c.InputTokens,
                outputTokens = c.OutputTokens,
                modelId = c.ModelId,
                batchIndex = c.BatchIndex,
                batchSize = c.BatchSize
            }).ToList()
        };

        return JsonSerializer.Serialize(metadata);
    }
}

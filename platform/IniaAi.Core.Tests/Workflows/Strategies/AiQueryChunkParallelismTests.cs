using System.Runtime.CompilerServices;
using System.Text;
using IniaAi.AiConnection.Abstractions;
using IniaAi.AiConnection.Common;
using IniaAi.AiConnection.Common.ChatConnector;
using IniaAi.Common.Enums;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing;
using NSubstitute;

namespace IniaAi.Core.Tests.Workflows.Strategies;

/// <summary>
/// Directly exercises the chunk-execution branch of <see cref="AiQueryStrategy"/> with a stub chat
/// connector (no AWS). The parallel path must produce byte-identical output AND identical merged
/// annotations to the sequential path for the same per-chunk responses — that equivalence is the
/// safety net, since chunk parallelism is always-on with no kill switch — and its chunk LLM calls
/// must actually overlap.
/// </summary>
public class AiQueryChunkParallelismTests
{
    private const int CHUNK_COUNT = 3;

    [Fact]
    public async Task ParallelChunks_MatchSequential_Output_Order_AndMergedFacts()
    {
        (Block sBlock, AiQueryProperties sProps, BlockExecutionContext sContext) = AiQueryChunkScenario.Build(StubConnector());
        var sequentialOutput = new StringBuilder();
        await AiQueryStrategy.ExecuteChunksSequentiallyAsync(
            sBlock, sProps, sContext, BuildChunks(), sequentialOutput, TestContext.Current.CancellationToken);

        (Block pBlock, AiQueryProperties pProps, BlockExecutionContext pContext) = AiQueryChunkScenario.Build(StubConnector());
        var parallelOutput = new StringBuilder();
        await AiQueryStrategy.ExecuteChunksInParallelAsync(
            pBlock, pProps, pContext, BuildChunks(), parallelOutput, TestContext.Current.CancellationToken);

        // Byte-identical combined response + debug input-context.
        Assert.Equal(sequentialOutput.ToString(), parallelOutput.ToString());
        Assert.Equal(sProps.ExecutionState!.InputContext, pProps.ExecutionState!.InputContext);

        // Merged annotations: same facts, same order (and non-empty, so the merge is really tested).
        Assert.NotEmpty(sContext.BlockAnnotations.Facts);
        Assert.Equal(
            sContext.BlockAnnotations.Facts.Select(f => f.Value),
            pContext.BlockAnnotations.Facts.Select(f => f.Value));

        // Iteration headers carry the chunk page numbers, so this proves the merge order is correct.
        string output = parallelOutput.ToString();
        int first = output.IndexOf("Iteration 1 (Page 1)", StringComparison.Ordinal);
        int second = output.IndexOf("Iteration 2 (Page 2)", StringComparison.Ordinal);
        int third = output.IndexOf("Iteration 3 (Page 3)", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < second && second < third, $"chunks merged out of order: {output}");
    }

    [Fact]
    public async Task ParallelChunks_OverlapButSequentialDoesNot()
    {
        StrongBox<int> parallelMax = new(0);
        (Block pBlock, AiQueryProperties pProps, BlockExecutionContext pContext) = AiQueryChunkScenario.Build(StubConnector(delayMs: 60, maxInFlight: parallelMax));
        await AiQueryStrategy.ExecuteChunksInParallelAsync(
            pBlock, pProps, pContext, BuildChunks(), new StringBuilder(), TestContext.Current.CancellationToken);

        Assert.True(parallelMax.Value >= 2, $"expected chunk overlap but max in-flight was {parallelMax.Value}");

        StrongBox<int> sequentialMax = new(0);
        (Block sBlock, AiQueryProperties sProps, BlockExecutionContext sContext) = AiQueryChunkScenario.Build(StubConnector(delayMs: 60, maxInFlight: sequentialMax));
        await AiQueryStrategy.ExecuteChunksSequentiallyAsync(
            sBlock, sProps, sContext, BuildChunks(), new StringBuilder(), TestContext.Current.CancellationToken);

        Assert.Equal(1, sequentialMax.Value);
    }

    [Fact]
    public async Task ConditionalTagBlock_RunsSequentially_AndEarlyExits()
    {
        StrongBox<int> callCount = new(0);
        (Block block, AiQueryProperties props, BlockExecutionContext context) =
            AiQueryChunkScenario.Build(StubConnector(callCount: callCount, responseText: "<DONE>found</DONE>"));
        props.ExecutionState!.ConditionalTag = "DONE";

        await AiQueryStrategy.ExecuteChunksSequentiallyAsync(
            block, props, context, BuildChunks(), new StringBuilder(), TestContext.Current.CancellationToken);

        // First chunk meets the tag → loop breaks → only one chunk's LLM call is made.
        Assert.Equal(1, callCount.Value);
    }

    [Fact]
    public async Task SequentialChunks_FormatExhaustion_SetsFailureDiagnosticsOnProps()
    {
        (Block block, AiQueryProperties props, BlockExecutionContext context) = AiQueryChunkScenario.Build(StubConnector());
        ApplyFailingFormatWithStaleDiagnostics(props);

        InvalidOperationException ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            AiQueryStrategy.ExecuteChunksSequentiallyAsync(
                block, props, context, BuildChunks(), new StringBuilder(), TestContext.Current.CancellationToken));

        AssertExhaustionDiagnostics(ex, props);
    }

    [Fact]
    public async Task ParallelChunks_FormatExhaustion_SetsFailureDiagnosticsOnProps()
    {
        (Block block, AiQueryProperties props, BlockExecutionContext context) = AiQueryChunkScenario.Build(StubConnector());
        ApplyFailingFormatWithStaleDiagnostics(props);

        InvalidOperationException ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            AiQueryStrategy.ExecuteChunksInParallelAsync(
                block, props, context, BuildChunks(), new StringBuilder(), TestContext.Current.CancellationToken));

        AssertExhaustionDiagnostics(ex, props);
    }

    [Fact]
    public async Task ParallelChunks_FirstChunkFailure_CancelsSiblingChunks()
    {
        // Chunk 1 exhausts its format retries near-instantly; siblings sit in a long first call.
        // Fail-fast must cancel them before their second attempt, so the total LLM call count stays
        // below the full fan-out cost (3 chunks x 2 attempts = 6).
        StrongBox<int> callCount = new(0);
        var connector = Substitute.For<IChatConnector>();
        _ = connector.GetResponseAsync((RequestContentSequence)null!, null, 0, false, null, null, null, TestContext.Current.CancellationToken)
            .ReturnsForAnyArgs(async callInfo =>
            {
                _ = Interlocked.Increment(ref callCount.Value);

                string request = callInfo.Arg<RequestContentSequence>()?.GetPlainText() ?? string.Empty;
                if (!request.Contains("Page 1 content", StringComparison.Ordinal))
                {
                    await SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
                }

                var response = new ResponseContentSequence();
                response.Add(new ResponseCommonContent("response without the required tag"));
                return response;
            });

        (Block block, AiQueryProperties props, BlockExecutionContext context) = AiQueryChunkScenario.Build(connector);
        props.OutputFormatType = OutputFormatType.SingleTag;
        props.OutputFormatTagName = "answer";

        InvalidOperationException ex = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            AiQueryStrategy.ExecuteChunksInParallelAsync(
                block, props, context, BuildChunks(), new StringBuilder(), TestContext.Current.CancellationToken));

        Assert.Contains("Output format validation failed", ex.Message);
        Assert.True(
            callCount.Value < CHUNK_COUNT * 2,
            $"expected sibling chunks to be cancelled, but {callCount.Value} LLM calls were made.");
    }

    private static void ApplyFailingFormatWithStaleDiagnostics(AiQueryProperties props)
    {
        // The stub response has no <answer> tag, so every attempt fails validation. Stale values
        // prove the run resets and then overwrites them.
        props.OutputFormatType = OutputFormatType.SingleTag;
        props.OutputFormatTagName = "answer";
        props.OutputFormatAttemptCount = 99;
        props.OutputFormatLastError = "stale-error";
        props.OutputFormatFailedAttemptResponse = "stale-response";
    }

    private static void AssertExhaustionDiagnostics(InvalidOperationException ex, AiQueryProperties props)
    {
        Assert.Contains("Output format validation failed", ex.Message);
        Assert.Equal(2, props.OutputFormatAttemptCount);
        Assert.NotNull(props.OutputFormatLastError);
        Assert.NotEqual("stale-error", props.OutputFormatLastError);
        Assert.Equal("extracted fact", props.OutputFormatFailedAttemptResponse);
        Assert.Null(props.ExtractedValues);
    }

    private static Dictionary<string, Dictionary<int, Page>> BuildChunks()
    {
        var fileMetadata = new FileMetadata("org", "test.pdf", 0, StorageProvider.Database);
        var document = new Document(fileMetadata, MediaType.ApplicationPdf);

        Dictionary<string, Dictionary<int, Page>> chunks = new(StringComparer.Ordinal);
        for (int i = 1; i <= CHUNK_COUNT; i++)
        {
            var page = new Page(document, i, i) { RawText = $"Page {i} content with extracted fact." };
            chunks[$"chunk{i}"] = new Dictionary<int, Page> { [i] = page };
        }

        return chunks;
    }

    /// <summary>Stub connector returning a fixed response (a fact with a source by default), with an
    /// in-flight counter and a call counter so concurrency and early-exit are observable.</summary>
    private static IChatConnector StubConnector(
        int delayMs = 0,
        StrongBox<int>? maxInFlight = null,
        StrongBox<int>? callCount = null,
        string responseText = "extracted fact")
    {
        var connector = Substitute.For<IChatConnector>();
        int inFlight = 0;
        object gate = new();

        async Task<ResponseContentSequence> RespondAsync()
        {
            if (callCount is not null)
            {
                lock (gate)
                {
                    callCount.Value++;
                }
            }

            int now = Interlocked.Increment(ref inFlight);
            if (maxInFlight is not null)
            {
                lock (gate)
                {
                    if (now > maxInFlight.Value)
                    {
                        maxInFlight.Value = now;
                    }
                }
            }

            if (delayMs > 0)
            {
                await SimulatedWork.TakesAsync(TimeSpan.FromMilliseconds(delayMs));
            }

            _ = Interlocked.Decrement(ref inFlight);

            var response = new ResponseContentSequence();
            response.Add(new ResponseCommonContent(responseText)
            {
                SourceReferences = [new SourceReference { Index = 0, CitedText = responseText }],
            });
            return response;
        }

        _ = connector.GetResponseAsync((RequestContentSequence)null!, null, 0, false, null, null, null, default)
            .ReturnsForAnyArgs(_ => RespondAsync());

        return connector;
    }
}

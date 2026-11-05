using IniaAi.Core.Tests.Workflows;
using IniaAi.Core.Workflows;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Tests.CitationTraces;

/// <summary>
/// Replays a fictional Parallel.ai deep-search result, shaped like a production one,
/// through WebApiCallStrategy + ParallelAiResponseParser
/// and records every transformation stage to a trace JSON for visualization.
///
/// Live calls aren't feasible here because Parallel.ai's API is task-based
/// (submit + poll + retrieve) and we only retain the retrieve step in the workflow
/// definition we have on hand.
/// </summary>
public class ParallelAiCitationTraceTests : IClassFixture<WorkflowsFixture<ParallelAiCitationTraceTests>>
{
    private const string FIXTURE_FILE = "CitationTraces/Fixtures/parallelai-deep-search-response.json";
    private const string PARALLEL_AI_RESULT_URL = "https://api.parallel.ai/v1/tasks/runs/trun_synthetic0000000000000000000002/result";

    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _outputHelper;

    public ParallelAiCitationTraceTests(
        WorkflowsFixture<ParallelAiCitationTraceTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _testServiceProvider = fixture.BuildTestServiceProvider(_outputHelper);
    }

    [Fact]
    public async Task Trace_ReplaysParallelAiDeepSearchFixture_AndCapturesEveryStageAsync()
    {
        // Arrange — load the fictional Parallel.ai response
        string fixturePath = Path.Combine(AppContext.BaseDirectory, FIXTURE_FILE);
        Assert.True(File.Exists(fixturePath), $"Fixture not found at {fixturePath} — check csproj copy directives");
        string responseBody = await File.ReadAllTextAsync(fixturePath, TestContext.Current.CancellationToken);

        var workflow = new Section("parallelai-trace", new Workflow());
        Block block = CitationTraceBlocks.ParallelAiResultBlock(
            workflow,
            "Market Deep Search Result",
            PARALLEL_AI_RESULT_URL,
            "Market_DeepSearch_Result");

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new FixtureWebApiCallStrategy(responseBody);

        // Act — execute through the real strategy + parser pipeline
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert — structural invariants
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(responseBody, block.BlockResponse.JsonResponse); // raw preserved
        Assert.NotEmpty(context.BlockAnnotations.Facts);
        Assert.All(context.BlockAnnotations.Facts, f => Assert.NotEmpty(f.Sources));
        Assert.Contains(context.BlockAnnotations.Facts, f => f.Sources.OfType<UrlSource>().Any());

        // Build the trace
        var parsedRaw = JObject.Parse(responseBody);
        int basisCount = (parsedRaw["output"]?["basis"] as JArray)?.Count ?? 0;
        int factsTotal = context.BlockAnnotations.Facts.Count;
        int factsNumerical = context.BlockAnnotations.Facts.Count(f => f.IsNumerical);
        int factsBroad = factsTotal - factsNumerical;

        var trace = new
        {
            provider = "parallel.ai",
            scenario = "deep-search-result-replay",
            note = "Fictional response shaped like a production deep-search result. " +
                   "Replayed deterministically because the start-the-task half of the Parallel.ai pattern was not in scope.",
            blockConfiguration = new
            {
                type = nameof(BlockTypes.WebApiCall),
                url = PARALLEL_AI_RESULT_URL,
                method = "GET",
                authHeader = "x-api-key (per-Organization key resolved via {Organization.ParallelApiKey} placeholder)",
                relatedBlock = "Market_DeepSearch_Start (POST that submits the task and writes run_id) — out of scope here",
            },
            stage1_rawResponse = new
            {
                description = "Unmodified body returned by api.parallel.ai/v1/tasks/runs/{id}/result",
                bodyLength = responseBody.Length,
                runStatus = parsedRaw["run"]?["status"]?.Value<string>(),
                runId = parsedRaw["run"]?["run_id"]?.Value<string>(),
                processor = parsedRaw["run"]?["processor"]?.Value<string>(),
                outputContentFieldCount = (parsedRaw["output"]?["content"] as JObject)?.Count ?? 0,
                basisEntryCount = basisCount,
            },
            stage2_parserOutput = new
            {
                description = "ParallelAiResponseParser.TryParse — walks output.basis[] and produces Fact[] with UrlSource citations",
                cleanedBodyEqualsRaw = block.BlockResponse.RawResponse == responseBody,
                factsTotal,
                factsBroad,
                factsNumerical,
                factsWithMultipleSources = context.BlockAnnotations.Facts.Count(f => f.Sources.Count > 1),
                uniqueSourceUrls = context.BlockAnnotations.Facts
                    .SelectMany(f => f.Sources.OfType<UrlSource>())
                    .Select(s => s.Url)
                    .Distinct()
                    .Count(),
            },
            stage3_blockAnnotationsFacts = context.BlockAnnotations.Facts.Select(f => new
            {
                value = f.Value,
                isNumerical = f.IsNumerical,
                sourceCount = f.Sources.Count,
                sources = f.Sources.Select(s => new
                {
                    type = s.Type.ToString(),
                    sourceKey = s.SourceKey,
                    citedText = s.CitedText,
                    url = (s as UrlSource)?.Url,
                }).ToArray(),
            }).ToArray(),
        };

        string outputPath = CitationTraceWriter.Write("parallel-ai-deep-search-trace.json", trace);
        _outputHelper.WriteLine($"Trace written: {outputPath}");
        _outputHelper.WriteLine($"basis entries: {basisCount}, facts total: {factsTotal} (numerical: {factsNumerical}, broad: {factsBroad})");
    }
}

using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class WebApiCallStrategyParallelAiTests : IClassFixture<WorkflowsFixture<WebApiCallStrategyParallelAiTests>>
{
    private const string PARALLEL_AI_BODY = """
        {
            "run": { "status": "completed" },
            "output": {
                "type": "json",
                "content": {
                    "executive_summary": "Total revenue was $12,278 million in 2024."
                },
                "basis": [
                    {
                        "field": "executive_summary",
                        "citations": [
                            { "title": "T1", "url": "https://example.com/report", "excerpts": ["Revenue: $12,278 million"] }
                        ],
                        "reasoning": "r",
                        "confidence": "high"
                    }
                ]
            }
        }
        """;

    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _outputHelper;

    public WebApiCallStrategyParallelAiTests(
        WorkflowsFixture<WebApiCallStrategyParallelAiTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _testServiceProvider = fixture.BuildTestServiceProvider(_outputHelper);
    }

    private sealed class StubWebApiCallStrategy : WebApiCallStrategy
    {
        private readonly string _cannedBody;
        public StubWebApiCallStrategy(string cannedBody)
        {
            _cannedBody = cannedBody;
        }
        protected override Task<string> ExecuteRequestAsync(
            HttpMessageHandler handler,
            string url,
            string method,
            string? payload,
            Dictionary<string, string> headers,
            string contentType,
            string acceptHeader,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_cannedBody);
        }
    }

    private (Block block, BlockExecutionContext context) SetupBlock(string urlTemplate)
    {
        var workflow = new Section("test", new Workflow("test"));
        var block = new Block(BlockTypes.WebApiCall, workflow)
        {
            Name = "Parallel.ai Result",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = urlTemplate,
                HttpMethod = "GET",
                ContentType = "application/json",
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        block.SetExecutionState(false, []);
        return (block, context);
    }

    [Fact]
    public async Task Execute_ParallelAiUrl_PopulatesBlockAnnotationsFactsAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://api.parallel.ai/v1/tasks/runs/trun_123/result");
        var strategy = new StubWebApiCallStrategy(PARALLEL_AI_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(context.BlockAnnotations.Facts);
        Assert.Contains(context.BlockAnnotations.Facts, f => f.Sources.OfType<UrlSource>().Any());
        Fact numerical = context.BlockAnnotations.Facts.First(f => f.IsNumerical && f.Value == "$12,278 million");
        Assert.Single(numerical.Sources);
    }

    [Fact]
    public async Task Execute_ParallelAiUrl_RawResponseEqualsOriginalBodyAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://api.parallel.ai/v1/tasks/runs/trun_123/result");
        var strategy = new StubWebApiCallStrategy(PARALLEL_AI_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(PARALLEL_AI_BODY, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task Execute_ParallelAiUrl_JsonResponseEqualsOriginalBodyAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://api.parallel.ai/v1/tasks/runs/trun_123/result");
        var strategy = new StubWebApiCallStrategy(PARALLEL_AI_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(PARALLEL_AI_BODY, block.BlockResponse.JsonResponse);
    }

    [Fact]
    public async Task Execute_ParallelAiUrlHostCaseInsensitiveAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://API.Parallel.AI/v1/tasks/runs/trun_123/result");
        var strategy = new StubWebApiCallStrategy(PARALLEL_AI_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_ParallelAiUrlWithQueryString_TriggersParseAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://api.parallel.ai/v1/tasks/runs/trun_123/result?foo=bar");
        var strategy = new StubWebApiCallStrategy(PARALLEL_AI_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_ParallelAiUrlWithTrailingSlash_TriggersParseAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://api.parallel.ai/v1/tasks/runs/trun_123/result/");
        var strategy = new StubWebApiCallStrategy(PARALLEL_AI_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_ParallelAiUrlPathCaseInsensitiveAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://api.parallel.ai/V1/Tasks/Runs/trun_123/Result");
        var strategy = new StubWebApiCallStrategy(PARALLEL_AI_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_NonParallelAiUrl_BehaviorUnchangedAsync()
    {
        const string RAW = """{"foo":"bar"}""";
        (Block block, BlockExecutionContext context) = SetupBlock("https://example.com/api");
        var strategy = new StubWebApiCallStrategy(RAW);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RAW, block.BlockResponse.RawResponse);
        Assert.Equal(RAW, block.BlockResponse.JsonResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_ParallelAiUrlButMalformedBody_FallsBackToRawOnlyAsync()
    {
        const string RAW = "not json";
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://api.parallel.ai/v1/tasks/runs/trun_123/result");
        var strategy = new StubWebApiCallStrategy(RAW);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RAW, block.BlockResponse.RawResponse);
        Assert.Equal(RAW, block.BlockResponse.JsonResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_ParallelAiUrlButMissingBasis_FallsBackToRawOnlyAsync()
    {
        const string RAW = """
            {
                "output": { "content": { "executive_summary": "Some text." } }
            }
            """;
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://api.parallel.ai/v1/tasks/runs/trun_123/result");
        var strategy = new StubWebApiCallStrategy(RAW);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RAW, block.BlockResponse.RawResponse);
        Assert.Equal(RAW, block.BlockResponse.JsonResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_ParallelAiUrlButEmptyBasis_FallsBackToRawOnlyAsync()
    {
        const string RAW = """
            {
                "output": {
                    "content": { "executive_summary": "Some text." },
                    "basis": []
                }
            }
            """;
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://api.parallel.ai/v1/tasks/runs/trun_123/result");
        var strategy = new StubWebApiCallStrategy(RAW);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RAW, block.BlockResponse.RawResponse);
        Assert.Equal(RAW, block.BlockResponse.JsonResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_ParallelAiUrlButAllBasisEntriesSkipped_FallsBackToRawOnlyAsync()
    {
        const string RAW = """
            {
                "output": {
                    "content": { "obj": { "nested": "value" } },
                    "basis": [
                        { "field": "obj", "citations": [] },
                        { "field": "missing.path", "citations": [] }
                    ]
                }
            }
            """;
        (Block block, BlockExecutionContext context) = SetupBlock(
            "https://api.parallel.ai/v1/tasks/runs/trun_123/result");
        var strategy = new StubWebApiCallStrategy(RAW);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RAW, block.BlockResponse.RawResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Theory]
    [InlineData("https://api.parallel.ai/v1/tasks/runs/trun_123")]
    [InlineData("https://api.parallel.ai/v1/tasks/runs/trun_123/status")]
    [InlineData("https://api.parallel.ai/v1/tasks/runs")]
    [InlineData("https://api.parallel.ai/v2/tasks/runs/trun_123/result")]
    [InlineData("https://api.parallel.ai/v1/tasks/runs/trun_123/result/subpath")]
    public async Task Execute_ParallelAiHostButDifferentEndpoint_FallsBackToRawOnlyAsync(string url)
    {
        (Block block, BlockExecutionContext context) = SetupBlock(url);
        var strategy = new StubWebApiCallStrategy(PARALLEL_AI_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(PARALLEL_AI_BODY, block.BlockResponse.RawResponse);
        Assert.Equal(PARALLEL_AI_BODY, block.BlockResponse.JsonResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }
}

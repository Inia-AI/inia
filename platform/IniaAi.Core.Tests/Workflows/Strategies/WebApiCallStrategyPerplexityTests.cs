using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Citations.Sources;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class WebApiCallStrategyPerplexityTests : IClassFixture<WorkflowsFixture<WebApiCallStrategyPerplexityTests>>
{
    private const string PERPLEXITY_BODY = """
        {
            "search_results": [
                { "title": "T1", "url": "https://example.com/one", "snippet": "Reports describe a deal worth $10.7 billion" },
                { "title": "T2", "url": "https://example.com/two", "snippet": "Confirmed total $10.7 billion in cash" }
            ],
            "choices": [
                {
                    "message": {
                        "role": "assistant",
                        "content": "The deal is worth $10.7 billion.[1][2]"
                    }
                }
            ]
        }
        """;

    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _outputHelper;

    public WebApiCallStrategyPerplexityTests(
        WorkflowsFixture<WebApiCallStrategyPerplexityTests> fixture,
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
            Name = "Perplexity Call",
            Properties = new WebApiCallProperties
            {
                UrlTemplate = urlTemplate,
                HttpMethod = "POST",
                RequestPayloadTemplate = """{"model":"sonar-pro","messages":[{"role":"user","content":"test"}]}""",
                ContentType = "application/json",
            },
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        block.SetExecutionState(false, []);
        return (block, context);
    }

    [Fact]
    public async Task Execute_PerplexityUrlWithValidResponse_PopulatesBlockAnnotationsFactsAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("https://api.perplexity.ai/chat/completions");
        var strategy = new StubWebApiCallStrategy(PERPLEXITY_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(context.BlockAnnotations.Facts);
        Assert.Contains(context.BlockAnnotations.Facts, f => f.Sources.OfType<UrlSource>().Any());
        Fact numerical = context.BlockAnnotations.Facts.First(f => f.IsNumerical && f.Value == "$10.7 billion");
        Assert.Equal(2, numerical.Sources.Count);
    }

    [Fact]
    public async Task Execute_PerplexityUrlWithValidResponse_RawResponseIsStrippedJsonAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("https://api.perplexity.ai/chat/completions");
        var strategy = new StubWebApiCallStrategy(PERPLEXITY_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.DoesNotContain("[1]", block.BlockResponse.RawResponse);
        Assert.DoesNotContain("[2]", block.BlockResponse.RawResponse);

        // RawResponse is still valid JSON with the full structure preserved
        var json = JObject.Parse(block.BlockResponse.RawResponse!);
        string? answer = json["choices"]?[0]?["message"]?["content"]?.Value<string>();
        Assert.Equal("The deal is worth $10.7 billion.", answer);
        Assert.NotNull(json["search_results"]);
    }

    [Fact]
    public async Task Execute_PerplexityUrlWithValidResponse_JsonResponseIsOriginalBodyAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("https://api.perplexity.ai/chat/completions");
        var strategy = new StubWebApiCallStrategy(PERPLEXITY_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(PERPLEXITY_BODY, block.BlockResponse.JsonResponse);
    }

    [Fact]
    public async Task Execute_PerplexityUrlHostCaseInsensitiveAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("https://API.Perplexity.AI/chat/completions");
        var strategy = new StubWebApiCallStrategy(PERPLEXITY_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(context.BlockAnnotations.Facts);
        Assert.NotNull(block.BlockResponse);
        Assert.DoesNotContain("[1]", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task Execute_NonPerplexityUrl_BehaviorUnchangedAsync()
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
    public async Task Execute_PerplexityUrlButMalformedResponseBody_FallsBackToRawOnlyAsync()
    {
        const string RAW = "not json";
        (Block block, BlockExecutionContext context) = SetupBlock("https://api.perplexity.ai/chat/completions");
        var strategy = new StubWebApiCallStrategy(RAW);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RAW, block.BlockResponse.RawResponse);
        Assert.Equal(RAW, block.BlockResponse.JsonResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_PerplexityUrlWithEmptySearchResults_FallsBackToRawOnlyAsync()
    {
        const string RAW = """
            {
                "search_results": [],
                "choices": [ { "message": { "role": "assistant", "content": "Hello.[1]" } } ]
            }
            """;
        (Block block, BlockExecutionContext context) = SetupBlock("https://api.perplexity.ai/chat/completions");
        var strategy = new StubWebApiCallStrategy(RAW);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RAW, block.BlockResponse.RawResponse);
        Assert.Equal(RAW, block.BlockResponse.JsonResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_PerplexityHostButDifferentEndpoint_FallsBackToRawOnlyAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("https://api.perplexity.ai/search");
        var strategy = new StubWebApiCallStrategy(PERPLEXITY_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(PERPLEXITY_BODY, block.BlockResponse.RawResponse);
        Assert.Equal(PERPLEXITY_BODY, block.BlockResponse.JsonResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_PerplexityChatCompletionsWithQueryString_TriggersParseAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("https://api.perplexity.ai/chat/completions?foo=bar");
        var strategy = new StubWebApiCallStrategy(PERPLEXITY_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(context.BlockAnnotations.Facts);
        Assert.NotNull(block.BlockResponse);
        Assert.DoesNotContain("[1]", block.BlockResponse.RawResponse);
        var json = JObject.Parse(block.BlockResponse.RawResponse!);
        Assert.NotNull(json["search_results"]);
    }

    [Fact]
    public async Task Execute_PerplexityUrlWithMissingChoices_FallsBackToRawOnlyAsync()
    {
        const string RAW = """
            {
                "search_results": [
                    { "title": "t1", "url": "https://example.com/a", "snippet": "s1" }
                ]
            }
            """;
        (Block block, BlockExecutionContext context) = SetupBlock("https://api.perplexity.ai/chat/completions");
        var strategy = new StubWebApiCallStrategy(RAW);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.Equal(RAW, block.BlockResponse.RawResponse);
        Assert.Equal(RAW, block.BlockResponse.JsonResponse);
        Assert.Empty(context.BlockAnnotations.Facts);
    }

    [Fact]
    public async Task Execute_PerplexityUrlWithTrailingSlash_TriggersParseAsync()
    {
        (Block block, BlockExecutionContext context) = SetupBlock("https://api.perplexity.ai/chat/completions/");
        var strategy = new StubWebApiCallStrategy(PERPLEXITY_BODY);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotEmpty(context.BlockAnnotations.Facts);
        Assert.NotNull(block.BlockResponse);
        Assert.DoesNotContain("[1]", block.BlockResponse.RawResponse);
    }
}

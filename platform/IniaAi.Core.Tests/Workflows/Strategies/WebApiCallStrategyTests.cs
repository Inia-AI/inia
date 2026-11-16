using IniaAi.Common.Http;
using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class WebApiCallStrategyTests
    : IClassFixture<WorkflowsFixture<WebApiCallStrategyTests>>, IClassFixture<LocalTestHttpServer>
{
    private readonly IServiceProvider _testServiceProvider;
    private readonly LocalTestHttpServer _server;

    public WebApiCallStrategyTests(
        WorkflowsFixture<WebApiCallStrategyTests> fixture,
        LocalTestHttpServer server,
        ITestOutputHelper outputHelper)
    {
        fixture.PlatformSettings.OutboundAccess.AllowedInternalHosts = ["127.0.0.1"];
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
        _server = server;
    }

    // Skips the retry backoff but keeps the production request timeout, so a loaded machine cannot
    // turn a slow answer into a timeout.
    private sealed class FastWebApiCallStrategy : WebApiCallStrategy
    {
        protected override Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

    private (Block Block, BlockExecutionContext Context) Arrange(WebApiCallProperties properties)
    {
        var block = new Block(BlockTypes.WebApiCall, new Section("test", new Workflow("test")))
        {
            Name = "Test",
            Properties = properties,
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        block.SetExecutionState(false, []);

        return (block, context);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidGetRequest_ReturnsResponseAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/get",
            HttpMethod = "GET",
        });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains("GET", block.BlockResponse.RawResponse);
        Assert.Contains("/get", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidPostRequest_ReturnsResponseAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/post",
            RequestPayloadTemplate = """{"test": "data"}""",
            HttpMethod = "POST",
            ContentType = "application/json",
        });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains("POST", block.BlockResponse.RawResponse);
        Assert.Contains("test", block.BlockResponse.RawResponse);
        Assert.Contains("data", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_CustomHeaders_SetsCorrectlyAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/headers",
            HttpMethod = "GET",
            AdditionalHeaders = new Dictionary<string, string>
            {
                { "X-Custom-Header", "test-value" },
                { "User-Agent", "test-agent" },
            },
        });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains("X-Custom-Header", block.BlockResponse.RawResponse);
        Assert.Contains("test-value", block.BlockResponse.RawResponse);
        Assert.Contains("test-agent", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_PostWithInvalidJsonPayload_ThrowsNamedLocalErrorAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/post",
            RequestPayloadTemplate = """{"query":"a "quoted" b"}""",
            HttpMethod = "POST",
            ContentType = "application/json",
        });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("payload", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("JSON", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteBlockAsync_GetWithInvalidJsonPayload_SkipsValidationAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/get",
            RequestPayloadTemplate = "not json at all",
            HttpMethod = "GET",
        });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_PostWithNonJsonContentType_SkipsPayloadValidationAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/post",
            RequestPayloadTemplate = "plain text body, not json",
            HttpMethod = "POST",
            ContentType = "text/plain",
        });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_PostNonJsonContentTypeWithEmbeddedPayloadPlaceholder_ThrowsNamedLocalErrorAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/post",
            RequestPayloadTemplate = "q={v}&limit=5",
            HttpMethod = "POST",
            ContentType = "text/plain",
        });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("JSON-escaped", exception.Message);
        Assert.Contains("text/plain", exception.Message);
    }

    [Fact]
    public async Task ExecuteBlockAsync_PostNonJsonContentTypeWholePlaceholderPayload_ExecutesAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/post",
            RequestPayloadTemplate = "{Body}",
            HttpMethod = "POST",
            ContentType = "text/plain",
        });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_RestrictedHeaderName_SkippedWithoutFailingBlockAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/headers",
            HttpMethod = "GET",
            AdditionalHeaders = new Dictionary<string, string>
            {
                { "Content-Length", "5" },
                { "X-Good", "kept" },
            },
        });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains("X-Good", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_InvalidHeaderName_SkippedWithoutFailingBlockAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/headers",
            HttpMethod = "GET",
            AdditionalHeaders = new Dictionary<string, string>
            {
                { "X Bad Name", "value" },
                { "X-Good", "kept" },
            },
        });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains("X-Good", block.BlockResponse.RawResponse);
        Assert.DoesNotContain("X Bad Name", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_AnAddressOutsideTheRunsAllowedSet_IsRefused_AndSendsNothing()
    {
        string path = $"/unapproved-{Guid.NewGuid():N}";
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}{path}",
        });
        context.AllowedWebOrigins = new HashSet<string>(["https://api.perplexity.ai"], StringComparer.OrdinalIgnoreCase);

        OutboundOriginRefusedException refusal = await Assert.ThrowsAsync<OutboundOriginRefusedException>(
            () => new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(OutboundOrigin.Of(new Uri(_server.BaseUrl)), refusal.Origin);
        Assert.Equal(0, _server.PathHits(path));
    }

    [Fact]
    public async Task ExecuteBlockAsync_AnAddressInTheRunsAllowedSet_IsSent()
    {
        string path = $"/approved-{Guid.NewGuid():N}";
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}{path}",
        });
        context.AllowedWebOrigins = new HashSet<string>([OutboundOrigin.Of(new Uri(_server.BaseUrl))], StringComparer.OrdinalIgnoreCase);

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Equal(1, _server.PathHits(path));
    }

    [Fact]
    public async Task ExecuteBlockAsync_AnAddressWithNoSchemeOrHost_ThrowsTheFixedHostProblem()
    {
        var properties = new WebApiCallProperties
        {
            UrlTemplate = "not-a-valid-url",
        };
        (Block block, BlockExecutionContext context) = Arrange(properties);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(properties.FixedHostProblems(block.Name).Single(), exception.Message);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UnsupportedMethod_ThrowsExceptionAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/anything",
            HttpMethod = "DELETE",
        });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("Unsupported HTTP method: DELETE", exception.Message);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EmptyUrl_ThrowsExceptionAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = "",
        });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("URL is required", exception.Message);
    }

    [Fact]
    public async Task ExecuteBlockAsync_DefaultValues_UsesDefaultsAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/get",
        });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains("GET", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WithDelay_WaitsCorrectTimeAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/get",
            HttpMethod = "GET",
            DelayAfterRequestSeconds = 2,
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.True(stopwatch.ElapsedMilliseconds >= 2000,
            $"Expected at least 2000ms delay, but took only {stopwatch.ElapsedMilliseconds}ms");
        Assert.True(stopwatch.ElapsedMilliseconds < 8000,
            $"Expected the 2s delay to be applied, but took {stopwatch.ElapsedMilliseconds}ms (delay multiplier wrong?)");
    }

    [Fact]
    public async Task ExecuteBlockAsync_InvalidDelayValue_IgnoresDelayAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/get",
            HttpMethod = "GET",
            DelayAfterRequestSeconds = null,
        });

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);
        stopwatch.Stop();

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.True(stopwatch.ElapsedMilliseconds < 2000,
            $"Expected no delay with null value, but took {stopwatch.ElapsedMilliseconds}ms");
    }

    [Fact]
    public async Task ExecuteBlockAsync_ServerError_RetriesAndEventuallyFailsAsync()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/status/500",
            HttpMethod = "GET",
        });

        HttpRequestException exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => new FastWebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("500", exception.Message);
    }

    [Fact]
    public async Task ExecuteBlockAsync_UsingProperties_ExecutesSuccessfully()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/headers",
            HttpMethod = "GET",
            ContentType = "application/json",
            AcceptHeader = "application/json",
            AdditionalHeaders = new Dictionary<string, string>
            {
                { "X-Test-Header", "properties-value" },
            },
        });

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains("X-Test-Header", block.BlockResponse.RawResponse);
        Assert.Contains("properties-value", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ARedirectFromAnAllowedHostToAnInternalAddress_IsRefused()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/redirect?to={Uri.EscapeDataString("http://169.254.169.254/latest/meta-data/")}",
            HttpMethod = "GET",
        });

        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => new FastWebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(InternalAddresses.LINK_LOCAL, refusal.Rule);
    }

    [Fact]
    public async Task ExecuteBlockAsync_AnInternalCompletionUrl_IsRefused()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/get",
            HttpMethod = "GET",
            PollUntilJsonPath = "method",
            PollUntilSuccessValues = "GET",
            PollCompletionUrlTemplate = "http://169.254.169.254/result",
        });

        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => new FastWebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(InternalAddresses.LINK_LOCAL, refusal.Rule);
    }

    [Fact]
    public async Task ExecuteBlockAsync_BlocksOfOneRun_ShareCookies_AndAnotherRunDoesNot()
    {
        (Block login, BlockExecutionContext loginContext) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/login?setCookie=session%3Drun-one",
            HttpMethod = "GET",
        });
        (Block sameRun, BlockExecutionContext sameRunContext) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/after-login",
            HttpMethod = "GET",
        });
        (Block otherRun, BlockExecutionContext otherRunContext) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/after-login",
            HttpMethod = "GET",
        });
        sameRunContext.WebApiCookies = loginContext.WebApiCookies;

        _ = await new WebApiCallStrategy().ExecuteBlockAsync(login, loginContext, TestContext.Current.CancellationToken);
        _ = await new WebApiCallStrategy().ExecuteBlockAsync(sameRun, sameRunContext, TestContext.Current.CancellationToken);
        _ = await new WebApiCallStrategy().ExecuteBlockAsync(otherRun, otherRunContext, TestContext.Current.CancellationToken);

        Assert.Contains("session=run-one", sameRun.BlockResponse!.RawResponse!);
        Assert.DoesNotContain("session=run-one", otherRun.BlockResponse!.RawResponse!);
    }

    [Theory]
    [InlineData(302, "GET")]
    [InlineData(303, "GET")]
    [InlineData(307, "POST")]
    [InlineData(308, "POST")]
    public async Task ExecuteBlockAsync_ARedirectBetweenAllowedHosts_IsFollowedWithTheMethodItKeeps(int status, string method)
    {
        string target = Uri.EscapeDataString($"{_server.BaseUrl}/echo-after-redirect");
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/redirect?status={status}&to={target}",
            RequestPayloadTemplate = """{"kept": "body"}""",
            HttpMethod = "POST",
            ContentType = "application/json",
            AdditionalHeaders = new Dictionary<string, string>
            {
                { "Authorization", "Bearer dropped-on-redirect" },
            },
        });

        _ = await new FastWebApiCallStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        string response = block.BlockResponse!.RawResponse!;
        Assert.Contains($"\"method\":\"{method}\"", response);
        Assert.Contains("/echo-after-redirect", response);
        Assert.DoesNotContain("dropped-on-redirect", response);
        Assert.Equal(method == "POST", response.Contains("kept"));
    }
}

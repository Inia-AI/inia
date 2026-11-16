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

public class WebApiCallStrategyInternalAddressTests
    : IClassFixture<WorkflowsFixture<WebApiCallStrategyInternalAddressTests>>, IClassFixture<LocalTestHttpServer>
{
    private readonly IServiceProvider _testServiceProvider;
    private readonly LocalTestHttpServer _server;

    public WebApiCallStrategyInternalAddressTests(
        WorkflowsFixture<WebApiCallStrategyInternalAddressTests> fixture,
        LocalTestHttpServer server,
        ITestOutputHelper outputHelper)
    {
        fixture.PlatformSettings.OutboundAccess.AllowedInternalHosts = [];
        _testServiceProvider = fixture.BuildTestServiceProvider(outputHelper);
        _server = server;
    }

    private sealed class RetryRecordingStrategy : WebApiCallStrategy
    {
        public List<TimeSpan> RetryWaits { get; } = [];

        protected override Task DelayBeforeRetryAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            RetryWaits.Add(delay);
            return Task.CompletedTask;
        }

        protected override Task DelayBetweenPollsAsync(int milliseconds, CancellationToken cancellationToken)
        {
            RetryWaits.Add(TimeSpan.FromMilliseconds(milliseconds));
            return Task.CompletedTask;
        }
    }

    private (Block Block, BlockExecutionContext Context) Arrange(
        WebApiCallProperties properties,
        Dictionary<string, string?>? placeholderValues = null)
    {
        var block = new Block(BlockTypes.WebApiCall, new Section("test", new Workflow("test")))
        {
            Name = "Test",
            Properties = properties,
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        block.SetExecutionState(placeholderValues is not null, placeholderValues ?? []);

        return (block, context);
    }

    [Fact]
    public async Task ExecuteBlockAsync_TheLoopbackTestServerWithNoAllowListEntry_FailsWithTheRefusalAndSendsNothing()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = $"{_server.BaseUrl}/never-reached",
            HttpMethod = "GET",
        });
        var strategy = new RetryRecordingStrategy();

        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains("127.0.0.1", refusal.Message);
        Assert.Contains(InternalAddresses.LOOPBACK, refusal.Message);
        Assert.Equal(0, _server.PathHits("/never-reached"));
        Assert.Empty(strategy.RetryWaits);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest/meta-data/", InternalAddresses.LINK_LOCAL)]
    [InlineData("http://2852039166/", InternalAddresses.LINK_LOCAL)]
    [InlineData("http://[fd00:ec2::254]/latest/meta-data/", InternalAddresses.UNIQUE_LOCAL)]
    [InlineData("http://[::1]/", InternalAddresses.LOOPBACK)]
    [InlineData("http://[::ffff:169.254.169.254]/", InternalAddresses.LINK_LOCAL)]
    [InlineData("https://localhost/", InternalAddresses.LOOPBACK)]
    public async Task ExecuteBlockAsync_AnInternalAddressInAnyUrlForm_FailsAtOnceWithItsRule(string url, string rule)
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = url,
            HttpMethod = "GET",
        });
        var strategy = new RetryRecordingStrategy();

        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(rule, refusal.Rule);
        Assert.Empty(strategy.RetryWaits);
    }

    // A previous block's output can fill a placeholder, so a placeholder may not reach the host: the block is
    // refused before any request, whatever the value would have been.
    [Theory]
    [InlineData("{part}", "http://169.254.169.254/metadata/identity/oauth2/token")]
    [InlineData("https://{part}/latest/meta-data/", "169.254.169.254")]
    [InlineData("http://{part}/admin", "127.0.0.1")]
    [InlineData("https://{part}/v3/quote", "example.invalid@127.0.0.1")]
    [InlineData("https://api.example.invalid{part}", "@127.0.0.1/v3/quote")]
    [InlineData("{part}://example.invalid/v3/quote", "http://127.0.0.1/#")]
    public async Task ExecuteBlockAsync_AWebAddressWhoseHostAPlaceholderFills_IsRefused_AndSendsNothing(string template, string value)
    {
        string path = $"/moved-{Guid.NewGuid():N}";
        (Block block, BlockExecutionContext context) = Arrange(
            new WebApiCallProperties { UrlTemplate = template, HttpMethod = "GET" },
            new Dictionary<string, string?> { ["part"] = value.Replace("127.0.0.1", new Uri(_server.BaseUrl).Authority, StringComparison.Ordinal) + path });
        var strategy = new RetryRecordingStrategy();

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(PropertiesOf(block).FixedHostProblems(block.Name).First(), failure.Message);
        Assert.Equal(0, _server.PathHits(path));
        Assert.Empty(strategy.RetryWaits);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ACompletionAddressWhoseHostAPlaceholderFills_IsRefused_BeforeTheFirstRequest()
    {
        string path = $"/start-{Guid.NewGuid():N}";
        (Block block, BlockExecutionContext context) = Arrange(
            new WebApiCallProperties
            {
                UrlTemplate = $"{_server.BaseUrl}{path}",
                HttpMethod = "GET",
                PollUntilJsonPath = "status",
                PollUntilSuccessValues = "done",
                PollCompletionUrlTemplate = "{Start.result_url}",
            },
            new Dictionary<string, string?> { ["Start.result_url"] = "http://169.254.169.254/result" });
        var strategy = new RetryRecordingStrategy();

        InvalidOperationException failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Contains(WebApiCallProperties.COMPLETION_URL_TEMPLATE_LABEL, failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, _server.PathHits(path));
    }

    [Fact]
    public async Task ExecuteBlockAsync_AnInternalPollUrl_FailsAtOnceInsteadOfPollingToTheDeadline()
    {
        (Block block, BlockExecutionContext context) = Arrange(new WebApiCallProperties
        {
            UrlTemplate = "http://169.254.169.254/status",
            HttpMethod = "GET",
            PollUntilJsonPath = "status",
            PollUntilSuccessValues = "done",
        });
        var strategy = new RetryRecordingStrategy();

        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(InternalAddresses.LINK_LOCAL, refusal.Rule);
        Assert.Empty(strategy.RetryWaits);
    }

    private static WebApiCallProperties PropertiesOf(Block block)
    {
        return (WebApiCallProperties)block.Properties;
    }
}

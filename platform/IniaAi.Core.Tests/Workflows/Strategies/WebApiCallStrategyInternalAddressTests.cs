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

    [Fact]
    public async Task ExecuteBlockAsync_AWholePlaceholderUrlFilledWithAnInternalAddress_IsRefused()
    {
        (Block block, BlockExecutionContext context) = Arrange(
            new WebApiCallProperties
            {
                UrlTemplate = "{target}",
                HttpMethod = "GET",
            },
            new Dictionary<string, string?> { ["target"] = "http://169.254.169.254/metadata/identity/oauth2/token" });

        OutboundAddressRefusedException refusal = await Assert.ThrowsAsync<OutboundAddressRefusedException>(
            () => new RetryRecordingStrategy().ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));

        Assert.Equal(InternalAddresses.LINK_LOCAL, refusal.Rule);
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
}

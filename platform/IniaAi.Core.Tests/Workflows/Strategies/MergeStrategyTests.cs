using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class MergeStrategyTests : IClassFixture<WorkflowsFixture<MergeStrategyTests>>
{
    private readonly IServiceProvider _testServiceProvider;
    private readonly WorkflowsFixture<MergeStrategyTests> _fixture;
    private readonly ITestOutputHelper _outputHelper;

    public MergeStrategyTests(WorkflowsFixture<MergeStrategyTests> blocksFixture, ITestOutputHelper outputHelper)
    {
        _fixture = blocksFixture;
        _outputHelper = outputHelper;
        _testServiceProvider = _fixture.BuildTestServiceProvider(_outputHelper);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidBlockAndContext_CallsApplyTemlateToDataMethodsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.Merge, workflow)
        {
            Name = "ApplyTemlateToData Query",
            Properties = new MergeProperties
            {
                TextTemplate = "abrdn plc"
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("abrdn plc", block.BlockResponse.RawResponse, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NullTextTemplate_LogsWarningAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.Merge, workflow)
        {
            Name = "ApplyTemlateToData Query",
            Properties = new MergeProperties
            {
                TextTemplate = null
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new MergeStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }
}

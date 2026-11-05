using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class SwitchCaseStrategyTests : IClassFixture<WorkflowsFixture<SwitchCaseStrategyTests>>
{
    private readonly IServiceProvider _testServiceProvider;
    private readonly ITestOutputHelper _outputHelper;

    public SwitchCaseStrategyTests(WorkflowsFixture<SwitchCaseStrategyTests> fixture, ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
        _testServiceProvider = fixture.BuildTestServiceProvider(_outputHelper);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ExactMatch_ReturnsCaseValue()
    {
        // Arrange
        (Block? block, BlockExecutionContext? context, SwitchCaseStrategy? strategy) = CreateTestSetup(
            input: "GAAP",
            cases: new Dictionary<string, string>
            {
                ["GAAP"] = "Generally Accepted Accounting Principles guide",
                ["IFRS"] = "International Financial Reporting Standards guide"
            });

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("Generally Accepted Accounting Principles guide", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_CaseInsensitiveMatch_ReturnsCaseValue()
    {
        // Arrange
        (Block? block, BlockExecutionContext? context, SwitchCaseStrategy? strategy) = CreateTestSetup(
            input: "gaap",
            cases: new Dictionary<string, string>
            {
                ["GAAP"] = "GAAP guide content",
                ["IFRS"] = "IFRS guide content"
            });

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("GAAP guide content", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_TrimmedMatch_ReturnsCaseValue()
    {
        // Arrange
        (Block? block, BlockExecutionContext? context, SwitchCaseStrategy? strategy) = CreateTestSetup(
            input: "  GAAP  ",
            cases: new Dictionary<string, string>
            {
                ["GAAP"] = "GAAP guide content"
            });

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("GAAP guide content", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NoMatch_ReturnsDefaultValue()
    {
        // Arrange
        (Block? block, BlockExecutionContext? context, SwitchCaseStrategy? strategy) = CreateTestSetup(
            input: "Unknown",
            cases: new Dictionary<string, string>
            {
                ["GAAP"] = "GAAP guide",
                ["IFRS"] = "IFRS guide"
            },
            defaultValue: "General accounting guide");

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("General accounting guide", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NoMatchAndNoDefault_ReturnsNoResponse()
    {
        // Arrange
        (Block? block, BlockExecutionContext? context, SwitchCaseStrategy? strategy) = CreateTestSetup(
            input: "Unknown",
            cases: new Dictionary<string, string>
            {
                ["GAAP"] = "GAAP guide"
            },
            defaultValue: null);

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NullInput_ReturnsNoResponse()
    {
        // Arrange
        (Block? block, BlockExecutionContext? context, SwitchCaseStrategy? strategy) = CreateTestSetup(
            input: null,
            cases: new Dictionary<string, string>
            {
                ["GAAP"] = "GAAP guide"
            });

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EmptyInput_ReturnsNoResponse()
    {
        // Arrange
        (Block? block, BlockExecutionContext? context, SwitchCaseStrategy? strategy) = CreateTestSetup(
            input: "   ",
            cases: new Dictionary<string, string>
            {
                ["GAAP"] = "GAAP guide"
            });

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NoCasesConfigured_ReturnsNoResponse()
    {
        // Arrange
        (Block? block, BlockExecutionContext? context, SwitchCaseStrategy? strategy) = CreateTestSetup(
            input: "GAAP",
            cases: []);

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_PlaceholderInCaseValue_ResolvesPlaceholders()
    {
        // Arrange
        (Block? block, BlockExecutionContext? context, SwitchCaseStrategy? strategy) = CreateTestSetup(
            input: "GAAP",
            cases: new Dictionary<string, string>
            {
                ["GAAP"] = "The standard is GAAP. Context: {previous_block}"
            });

        // Add resolved placeholder value to context
        context.TextInputs["previous_block"] = "fiscal year 2024";

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("The standard is GAAP. Context: fiscal year 2024", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_TrimmedCaseKey_MatchesCorrectly()
    {
        // Arrange - case key itself has whitespace
        (Block? block, BlockExecutionContext? context, SwitchCaseStrategy? strategy) = CreateTestSetup(
            input: "GAAP",
            cases: new Dictionary<string, string>
            {
                [" GAAP "] = "GAAP guide with trimmed key"
            });

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("GAAP guide with trimmed key", block.BlockResponse.RawResponse);
    }

    private (Block block, BlockExecutionContext context, SwitchCaseStrategy strategy) CreateTestSetup(
        string? input,
        Dictionary<string, string> cases,
        string? defaultValue = null)
    {
        var workflow = new Section("test-org", new Workflow("test-org"));
        var block = new Block(BlockTypes.SwitchCase, workflow)
        {
            Name = "SwitchCase Test",
            Properties = new SwitchCaseProperties
            {
                Cases = cases,
                DefaultValue = defaultValue,
                InputTemplate = input,
            }
        };

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new SwitchCaseStrategy();

        return (block, context, strategy);
    }
}

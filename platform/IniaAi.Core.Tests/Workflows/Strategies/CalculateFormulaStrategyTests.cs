using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class CalculateFormulaStrategyTests : IClassFixture<WorkflowsFixture<CalculateFormulaStrategyTests>>
{
    private readonly IServiceProvider _testServiceProvider;
    private readonly WorkflowsFixture<CalculateFormulaStrategyTests> _fixture;
    private readonly ITestOutputHelper _outputHelper;

    public CalculateFormulaStrategyTests(WorkflowsFixture<CalculateFormulaStrategyTests> blocksFixture, ITestOutputHelper outputHelper)
    {
        _fixture = blocksFixture;
        _outputHelper = outputHelper;
        _testServiceProvider = _fixture.BuildTestServiceProvider(_outputHelper);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidBlockAndContext_CallsCalculateFormulaMethodsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "2.2 + 2.2"
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new CalculateFormulaStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("4.4", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidBlockAndContextWithDecimals_UsesInvariantCultureAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "2.2 + 2.7"
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new CalculateFormulaStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("4.9", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_AbsoluteValue_ReturnsPositiveAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "abs(-5)"
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new CalculateFormulaStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal("5", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NullTextTemplate_LogsWarningAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = null
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new CalculateFormulaStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EmptyTextTemplate_LogsWarningAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = string.Empty
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new CalculateFormulaStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_FormulaContainsNoDataMarker_LogsWarningAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        string noDataText = context.PlatformSettings.IniaAi.NoDataForPlaceHolderText;
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = $"addDays({noDataText}, 30)"
            }
        };
        var strategy = new CalculateFormulaStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(block.BlockResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_InvalidTextTemplate_ThrowsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "2 +"
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new CalculateFormulaStrategy();

        // Act & Assert
        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));
        Assert.Contains("Unable to process the formula", exception.Message);
    }

    [Theory]
    [InlineData("addDays('2024-01-01', 30)", "2024-01-31")]
    [InlineData("addDays(#2024/01/01#, 30)", "2024-01-31")]
    [InlineData("addDays('2024-01-31', -30)", "2024-01-01")]
    [InlineData("addDays(2025-04-15, 10)", "2025-04-25")]
    [InlineData("diffDays(2024-02-01, 2024-01-01)", "31")]
    [InlineData("addMonths('2024-01-31', 1)", "2024-02-29")]
    [InlineData("addMonths('2023-01-31', 1)", "2023-02-28")]
    [InlineData("addYears('2024-02-29', 1)", "2025-02-28")]
    [InlineData("diffDays('2024-02-01', '2024-01-01')", "31")]
    [InlineData("year('2024-06-15')", "2024")]
    [InlineData("month('2024-06-15')", "6")]
    [InlineData("day('2024-06-15')", "15")]
    [InlineData("startOfMonth('2024-06-15')", "2024-06-01")]
    [InlineData("endOfMonth('2024-02-15')", "2024-02-29")]
    [InlineData("diffDays(endOfMonth('2024-01-15'), startOfMonth('2024-01-15')) + 1", "31")]
    public async Task ExecuteBlockAsync_DateFormula_ReturnsExpectedResultAsync(string formula, string expected)
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = formula
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new CalculateFormulaStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Equal(expected, block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_Today_ReturnsIsoDateStringAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "today()"
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new CalculateFormulaStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_InvalidDateString_ThrowsAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.CalculateNumber, workflow)
        {
            Name = "Calculate Formula Block",
            Properties = new CalculateFormulaProperties
            {
                FormulaTemplate = "addDays('not-a-date', 1)"
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new CalculateFormulaStrategy();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken));
    }
}

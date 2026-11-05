using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class DynamicChartStrategyTests : IClassFixture<WorkflowsFixture<DynamicChartStrategyTests>>
{
    private const string CHART_NAME = "Peer spread";
    private const string DATA_WITH_NOT_A_NUMBER =
        "[{\"period\":\"1M\",\"value\":3.07},{\"period\":\"3M\",\"value\":\"N/A\"},{\"period\":\"1Y\",\"value\":\"44.5\"}]";
    private const string DATA_LEADING_WITH_NOT_A_NUMBER =
        "[{\"period\":\"Q1\",\"value\":\"N/A\"},{\"period\":\"Q2\",\"value\":5}]";
    private const string DATA_WITH_ONLY_NOT_A_NUMBER =
        "[{\"period\":\"1M\",\"value\":\"N/A\"},{\"period\":\"3M\",\"value\":null}]";

    private const string STATIC_DATA_SOURCE_JSON =
        "[{\"period\":\"1M\",\"value\":3.07},{\"period\":\"3M\",\"value\":-1.37},{\"period\":\"1Y\",\"value\":44.5}]";

    private readonly IServiceProvider _testServiceProvider;
    private readonly WorkflowsFixture<DynamicChartStrategyTests> _fixture;
    private readonly ITestOutputHelper _outputHelper;

    public DynamicChartStrategyTests(
        WorkflowsFixture<DynamicChartStrategyTests> fixture,
        ITestOutputHelper outputHelper)
    {
        _fixture = fixture;
        _outputHelper = outputHelper;
        _testServiceProvider = _fixture.BuildTestServiceProvider(_outputHelper);
    }

    [Fact]
    public async Task ExecuteBlockAsync_StaticDataSourceJson_GeneratesChartAsync()
    {
        // Arrange — control case: DataSourceJson is hardcoded JSON, no placeholders
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.DynamicChart, workflow)
        {
            Name = "Static Chart",
            Properties = new DynamicChartProperties
            {
                DataSourceJson = STATIC_DATA_SOURCE_JSON,
                ChartType = "column",
                XAxisField = "period",
                YAxisField = "value",
                ChartTitle = "Static Test Chart"
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new DynamicChartStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.False(block.ExecutionState?.HasFailed ?? false);
        Assert.Contains("\"period\"", block.BlockResponse.RawResponse);
    }

    /// <summary>
    /// Reproduces the bug where DynamicChartStrategy reads <c>props.DataSourceJson</c>
    /// (the unresolved template) instead of <c>props.ExecutionState.DataSourceJson</c>
    /// (the resolved value), as the property class itself documents.
    ///
    /// In production, the workflow handler resolves placeholders into ExecutionState
    /// via <see cref="DynamicChartProperties.SetExecutionState"/>; the strategy then
    /// runs and finds the placeholder still literal in <c>props.DataSourceJson</c>,
    /// which makes <c>JToken.Parse</c> fail at the dot before the field accessor
    /// (e.g. <c>{block.answer}</c> ⇒ "Invalid JavaScript property identifier
    /// character: ." at position of the dot).
    ///
    /// When the strategy is fixed to read from ExecutionState, this test will pass.
    /// </summary>
    [Fact]
    public async Task ExecuteBlockAsync_PlaceholderDataSourceJson_ResolvesFromExecutionStateAsync()
    {
        // Arrange — DataSourceJson holds an unresolved placeholder template; the
        // ExecutionState carries the resolved JSON (this models what the runtime
        // does after SetExecutionState runs against the inputs dictionary).
        var props = new DynamicChartProperties
        {
            DataSourceJson = "{chartData.answer}",
            ChartType = "column",
            XAxisField = "period",
            YAxisField = "value",
            ChartTitle = "Resolved Chart"
        };
        props.ExecutionState = new DynamicChartPropertiesExecutionState(props)
        {
            DataSourceJson = STATIC_DATA_SOURCE_JSON
        };

        // Sanity-check the test setup: template untouched, ExecutionState resolved.
        Assert.Equal("{chartData.answer}", props.DataSourceJson);
        Assert.Equal(STATIC_DATA_SOURCE_JSON, props.ExecutionState.DataSourceJson);

        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.DynamicChart, workflow)
        {
            Name = "Placeholder Chart",
            Properties = props
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new DynamicChartStrategy();

        // Act + Assert — should succeed and produce a chart from the resolved data.
        // Currently throws InvalidOperationException because the strategy reads the
        // template ("{chartData.answer}") from props.DataSourceJson and tries to
        // parse it as JSON, instead of reading the resolved value from ExecutionState
        // as the property class itself documents.
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.NotNull(block.BlockResponse);
        Assert.False(block.ExecutionState?.HasFailed ?? false);
        Assert.Contains("\"period\"", block.BlockResponse.RawResponse);
    }

    /// <summary>
    /// Covers placeholder resolution for the non-data templated fields
    /// (XAxisField, YAxisField, ChartTitle, axis labels). These fields go into
    /// the chart config string output, so a regression to reading from
    /// <c>props</c> would surface as the literal placeholder appearing in the
    /// rendered config rather than the resolved value.
    /// </summary>
    [Fact]
    public async Task ExecuteBlockAsync_PlaceholderAxisAndLabelFields_ResolveFromExecutionStateAsync()
    {
        // Arrange — every templated field on Properties holds an unresolved
        // placeholder; ExecutionState carries the resolved value the strategy
        // should actually use.
        var props = new DynamicChartProperties
        {
            DataSourceJson = "{chartData.answer}",
            ChartType = "column",
            XAxisField = "{xField.value}",
            YAxisField = "{yField.value}",
            ChartTitle = "{title.value}",
            XAxisLabel = "{xLabel.value}",
            YAxisLabel = "{yLabel.value}"
        };
        props.ExecutionState = new DynamicChartPropertiesExecutionState(props)
        {
            DataSourceJson = STATIC_DATA_SOURCE_JSON,
            XAxisField = "period",
            YAxisField = "value",
            ChartTitle = "Resolved Title",
            XAxisLabel = "Resolved X Label",
            YAxisLabel = "Resolved Y Label"
        };

        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.DynamicChart, workflow)
        {
            Name = "Placeholder Axis Chart",
            Properties = props
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new DynamicChartStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert — block executed successfully and the rendered chart config
        // contains the resolved values, not the literal placeholder strings.
        Assert.NotNull(block.BlockResponse);
        Assert.False(block.ExecutionState?.HasFailed ?? false);
        string raw = block.BlockResponse.RawResponse
            ?? throw new InvalidOperationException("RawResponse should be populated");
        Assert.Contains("Resolved Title", raw);
        Assert.Contains("Resolved X Label", raw);
        Assert.Contains("Resolved Y Label", raw);
        Assert.DoesNotContain("{xField.value}", raw);
        Assert.DoesNotContain("{yField.value}", raw);
        Assert.DoesNotContain("{title.value}", raw);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NotANumber_NotCritical_PlotsZero()
    {
        (Block block, BlockExecutionContext context, DynamicChartStrategy strategy) =
            CreateChartSetup(DATA_WITH_NOT_A_NUMBER, isCritical: null);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        ChartConfiguration chart = ReadChart(block);
        Assert.Equal(["1M", "3M", "1Y"], chart.Data.Select(point => point.X));
        Assert.Equal([3.07, 0, 44.5], chart.Data.Select(point => point.Y));
        Assert.Null(block.ExecutionState?.Error);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NotANumber_Critical_DropsThePointAndKeepsTheOthers()
    {
        (Block block, BlockExecutionContext context, DynamicChartStrategy strategy) =
            CreateChartSetup(DATA_WITH_NOT_A_NUMBER, isCritical: true);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        ChartConfiguration chart = ReadChart(block);
        Assert.Equal(["1M", "1Y"], chart.Data.Select(point => point.X));
        Assert.Equal([3.07, 44.5], chart.Data.Select(point => point.Y));
        Assert.Null(block.ExecutionState?.Error);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NoNumbers_NotCritical_ProducesAChartOfZeros()
    {
        (Block block, BlockExecutionContext context, DynamicChartStrategy strategy) =
            CreateChartSetup(DATA_WITH_ONLY_NOT_A_NUMBER, isCritical: false);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        ChartConfiguration chart = ReadChart(block);
        Assert.Equal([0d, 0d], chart.Data.Select(point => point.Y));
        Assert.Null(block.ExecutionState?.Error);
    }

    [Fact]
    public async Task ExecuteBlockAsync_LeadingNotANumber_Critical_TakesTheYAxisTypeFromThePlottedRows()
    {
        (Block block, BlockExecutionContext context, DynamicChartStrategy strategy) =
            CreateChartSetup(DATA_LEADING_WITH_NOT_A_NUMBER, isCritical: true);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        ChartConfiguration chart = ReadChart(block);
        Assert.Equal(["Q2"], chart.Data.Select(point => point.X));
        Assert.Equal("linear", chart.YAxis.Type);
    }

    [Fact]
    public async Task ExecuteBlockAsync_LeadingNotANumber_NotCritical_KeepsTheYAxisTypeOfTheFirstRow()
    {
        (Block block, BlockExecutionContext context, DynamicChartStrategy strategy) =
            CreateChartSetup(DATA_LEADING_WITH_NOT_A_NUMBER, isCritical: null);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        ChartConfiguration chart = ReadChart(block);
        Assert.Equal([0d, 5d], chart.Data.Select(point => point.Y));
        Assert.Equal("category", chart.YAxis.Type);
    }

    [Theory]
    [InlineData(DATA_WITH_ONLY_NOT_A_NUMBER, "value")]
    [InlineData(STATIC_DATA_SOURCE_JSON, "missing.field")]
    [InlineData("[]", "value")]
    public async Task ExecuteBlockAsync_NothingToPlot_Critical_FailsTheBlock(string dataSourceJson, string yAxisField)
    {
        (Block block, BlockExecutionContext context, DynamicChartStrategy strategy) =
            CreateChartSetup(dataSourceJson, isCritical: true, yAxisField);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        Assert.Null(block.BlockResponse);
        Assert.NotNull(block.ExecutionState);
        Assert.Equal(
            "The Dynamic Chart block 'Peer spread' found no numbers to plot, so it failed. "
            + $"Check that its data holds the X-axis field 'period' and a number in the Y-axis field '{yAxisField}'.",
            block.ExecutionState.Error);
    }

    [Theory]
    [InlineData(STATIC_DATA_SOURCE_JSON, "missing.field")]
    [InlineData("[]", "value")]
    public async Task ExecuteBlockAsync_NothingToPlot_NotCritical_ProducesAnEmptyChart(string dataSourceJson, string yAxisField)
    {
        (Block block, BlockExecutionContext context, DynamicChartStrategy strategy) =
            CreateChartSetup(dataSourceJson, isCritical: null, yAxisField);

        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        ChartConfiguration chart = ReadChart(block);
        Assert.Empty(chart.Data);
        Assert.Null(block.ExecutionState?.Error);
    }

    private (Block block, BlockExecutionContext context, DynamicChartStrategy strategy) CreateChartSetup(
        string dataSourceJson,
        bool? isCritical,
        string yAxisField = "value")
    {
        var section = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.DynamicChart, section)
        {
            Name = CHART_NAME,
            Properties = new DynamicChartProperties
            {
                DataSourceJson = dataSourceJson,
                ChartType = "column",
                XAxisField = "period",
                YAxisField = yAxisField,
                IsCritical = isCritical,
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        return (block, context, new DynamicChartStrategy());
    }

    private static ChartConfiguration ReadChart(Block block)
    {
        Assert.NotNull(block.BlockResponse);
        string raw = block.BlockResponse.RawResponse
            ?? throw new InvalidOperationException("RawResponse should be populated");
        return JsonConvert.DeserializeObject<ChartConfiguration>(raw)
            ?? throw new InvalidOperationException("RawResponse should hold a chart configuration");
    }
}

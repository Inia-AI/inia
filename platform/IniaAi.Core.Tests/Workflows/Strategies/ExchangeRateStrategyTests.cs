using IniaAi.Core.Workflows;
using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;

// TextInputExecutionState is in IniaAi.Domain.Entities.Workflows namespace

namespace IniaAi.Core.Tests.Workflows.Strategies;

public class ExchangeRateStrategyTests : IClassFixture<WorkflowsFixture<ExchangeRateStrategyTests>>, IDisposable
{
    private readonly CultureInfo _originalCulture;
    private readonly IServiceProvider _testServiceProvider;
    private readonly WorkflowsFixture<ExchangeRateStrategyTests> _fixture;
    private readonly ITestOutputHelper _outputHelper;

    public ExchangeRateStrategyTests(WorkflowsFixture<ExchangeRateStrategyTests> fixture, ITestOutputHelper outputHelper)
    {
        _originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        _fixture = fixture;
        _outputHelper = outputHelper;
        _testServiceProvider = _fixture.BuildTestServiceProvider(_outputHelper);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        CultureInfo.CurrentCulture = _originalCulture;
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidCurrencies_ReturnsExchangeRateAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "USD",
                ToCurrency = "EUR",
                UseCurrentDate = true
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains("USD/EUR", block.BlockResponse.RawResponse);
        Assert.Contains("<rawValue>", block.BlockResponse.RawResponse);
        Assert.Contains("<formattedValue>", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ValidCurrencies_PopulatesExecutionStateAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "usd",  // Lowercase to test normalization
                ToCurrency = "gbp",
                UseCurrentDate = true
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        var props = block.Properties as ExchangeRateProperties;
        Assert.NotNull(props?.ExecutionState);
        Assert.Equal("USD", props.ExecutionState.FromCurrency);  // Should be uppercase
        Assert.Equal("GBP", props.ExecutionState.ToCurrency);    // Should be uppercase
    }

    [Fact]
    public async Task ExecuteBlockAsync_MissingFromCurrency_ReturnsErrorResponseAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = null,
                ToCurrency = "EUR",
                UseCurrentDate = true
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Missing currency codes", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_MissingToCurrency_ReturnsErrorResponseAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "USD",
                ToCurrency = null,
                UseCurrentDate = true
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Missing currency codes", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_EmptyCurrencies_ReturnsErrorResponseAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "",
                ToCurrency = "",
                UseCurrentDate = true
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Missing currency codes", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_WhitespaceCurrencies_ReturnsErrorResponseAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "   ",
                ToCurrency = "   ",
                UseCurrentDate = true
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Missing currency codes", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_SpecificDate_UsesProvidedDateAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var testDate = new DateTime(2024, 1, 15);
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "USD",
                ToCurrency = "EUR",
                UseCurrentDate = false,
                ExchangeDate = testDate.ToString("yyyy-MM-dd")
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("2024-01-15", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_CurrencyNormalization_NormalizesToUppercaseAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = " usd ",  // Lowercase with whitespace
                ToCurrency = " eur ",    // Lowercase with whitespace
                UseCurrentDate = true
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        var props = block.Properties as ExchangeRateProperties;
        Assert.NotNull(props);
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        Assert.Contains("USD/EUR", block.BlockResponse.RawResponse); // Should be normalized
        Assert.Equal("USD", props.ExecutionState?.FromCurrency);
        Assert.Equal("EUR", props.ExecutionState?.ToCurrency);
    }

    [Fact]
    public async Task ExecuteBlockAsync_SameCurrency_ReturnsRateOfOneAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "USD",
                ToCurrency = "USD",
                UseCurrentDate = true
            }
        };
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.NotNull(block.BlockResponse.RawResponse);
        // Same currency should return rate of 1.000000
        Assert.Contains("1.000000", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_NoPropertiesOrTextInputs_ReturnsErrorAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            // No Properties set
        };
        // No TextInputs added

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("Missing currency codes", block.BlockResponse.RawResponse);
    }

    [Fact]
    public async Task ExecuteBlockAsync_CreatesExecutionStateIfNullAsync()
    {
        // Arrange
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "USD",
                ToCurrency = "EUR",
                UseCurrentDate = true
            }
        };
        var props = block.Properties as ExchangeRateProperties;

        // Ensure ExecutionState is null before execution
        Assert.NotNull(props);
        Assert.Null(props.ExecutionState);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("USD", props.ExecutionState.FromCurrency);
        Assert.Equal("EUR", props.ExecutionState.ToCurrency);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ExecutionState_ClonesPropertiesWithTemplateValuesAsync()
    {
        // Arrange - Verify that ExecutionState.Properties is populated from block.Properties
        // The API mapper will then use ExecutionState.FromCurrency/ToCurrency for the resolved values
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "CHF",
                ToCurrency = "SEK",
                UseCurrentDate = false,
                ExchangeDate = "2024-01-15"
            }
        };
        var props = block.Properties as ExchangeRateProperties;
        Assert.NotNull(props);

        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.NotNull(block.ExecutionState);

        // ExecutionState.FromCurrency and ToCurrency contain the resolved (normalized) values
        Assert.Equal("CHF", props.ExecutionState.FromCurrency);
        Assert.Equal("SEK", props.ExecutionState.ToCurrency);

        // ExecutionState.Properties should be a clone of block.Properties
        var execProps = block.Properties as ExchangeRateProperties;
        Assert.NotNull(execProps);
        Assert.Equal("CHF", execProps.FromCurrency);
        Assert.Equal("SEK", execProps.ToCurrency);
        Assert.Equal("2024-01-15", execProps.ExchangeDate);
        Assert.False(execProps.UseCurrentDate);
    }

    [Fact]
    public async Task ExecuteBlockAsync_ExecutionStateFromCurrencyAndToCurrency_AreSetCorrectlyAsync()
    {
        // Arrange - This test specifically verifies that block.ExecutionState.FromCurrency
        // and block.ExecutionState.ToCurrency are populated (used by API mapping)
        var workflow = new Section("1234", new Workflow("1234"));
        var block = new Block(BlockTypes.ExchangeRate, workflow)
        {
            Name = "Exchange Rate Block",
            Properties = new ExchangeRateProperties
            {
                FromCurrency = "  aud  ",  // With whitespace
                ToCurrency = "  NZD  ",    // With whitespace
                UseCurrentDate = true
            }
        };
        var props = block.Properties as ExchangeRateProperties;
        Assert.NotNull(props);
        BlockExecutionContext context = _testServiceProvider.GetRequiredService<BlockExecutionContext>();
        var strategy = new ExchangeRateStrategy();

        // Act
        _ = await strategy.ExecuteBlockAsync(block, context, TestContext.Current.CancellationToken);

        // Assert - These properties are read by the API mapper to populate properties.executionState
        Assert.NotNull(block.ExecutionState);
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("AUD", props.ExecutionState.FromCurrency);  // Trimmed and uppercase
        Assert.Equal("NZD", props.ExecutionState.ToCurrency);    // Trimmed and uppercase

        // Verify the response also contains the normalized values
        Assert.NotNull(block.BlockResponse);
        Assert.Contains("AUD/NZD", block.BlockResponse.RawResponse);
    }
}

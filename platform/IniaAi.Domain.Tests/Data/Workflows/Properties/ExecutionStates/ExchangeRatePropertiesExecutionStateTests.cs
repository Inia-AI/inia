using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.ExecutionStates;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties.ExecutionStates;

public class ExchangeRatePropertiesExecutionStateTests
{
    [Fact]
    public void Constructor_CopiesAllFieldsFromSource()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "from {tag}",
            ToCurrency = "to {tag}",
            ExchangeDate = "date {tag}",
            InputValue = "value {tag}"
        };

        // Act
        var state = new ExchangeRatePropertiesExecutionState(props);

        // Assert
        Assert.Equal("from {tag}", state.FromCurrency);
        Assert.Equal("to {tag}", state.ToCurrency);
        Assert.Equal("date {tag}", state.ExchangeDate);
        Assert.Equal("value {tag}", state.InputValue);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInAllFields()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "prefix {tag} suffix",
            ToCurrency = "prefix {tag} suffix",
            ExchangeDate = "prefix {tag} suffix",
            InputValue = "prefix {tag} suffix"
        };
        var state = new ExchangeRatePropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.Equal("prefix resolved suffix", state.FromCurrency);
        Assert.Equal("prefix resolved suffix", state.ToCurrency);
        Assert.Equal("prefix resolved suffix", state.ExchangeDate);
        Assert.Equal("prefix resolved suffix", state.InputValue);
    }

    [Fact]
    public void ReplaceAllPlaceholders_NullFields_DoesNotThrow()
    {
        // Arrange
        var props = new ExchangeRateProperties();
        var state = new ExchangeRatePropertiesExecutionState(props);

        // Act & Assert — should not throw
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?> { { "tag", "resolved" } });
        Assert.Null(state.FromCurrency);
        Assert.Null(state.ToCurrency);
        Assert.Null(state.ExchangeDate);
        Assert.Null(state.InputValue);
    }

    [Fact]
    public void SetExecutionState_OnProperties_CreatesAndResolvesState()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "prefix {tag} suffix",
            ToCurrency = "prefix {tag} suffix"
        };

        // Act
        props.SetExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.FromCurrency);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.ToCurrency);
    }

    [Fact]
    public void Constructor_CopiesToCurrenciesList_Independently()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            ToCurrencies = ["GBP", "CHF"]
        };

        // Act
        var state = new ExchangeRatePropertiesExecutionState(props);
        state.ToCurrencies!.Add("JPY");

        // Assert - twin has its own list; source unaffected
        Assert.Equal(["GBP", "CHF"], props.ToCurrencies);
        Assert.Equal(["GBP", "CHF", "JPY"], state.ToCurrencies);
    }

    [Fact]
    public void Constructor_NullToCurrencies_StaysNull()
    {
        // Arrange
        var props = new ExchangeRateProperties();

        // Act
        var state = new ExchangeRatePropertiesExecutionState(props);

        // Assert
        Assert.Null(state.ToCurrencies);
    }

    [Fact]
    public void ReplaceAllPlaceholders_ReplacesInToCurrenciesItems()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            ToCurrencies = ["{first}", "GBP", "{second}"]
        };
        var state = new ExchangeRatePropertiesExecutionState(props);

        // Act
        state.ReplaceAllPlaceholders(true, new Dictionary<string, string?>
        {
            { "first", "EUR" },
            { "second", "CHF" }
        });

        // Assert
        Assert.Equal(["EUR", "GBP", "CHF"], state.ToCurrencies);
    }

    [Fact]
    public void UpdateExecutionState_WhenNull_CreatesState()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "prefix {tag} suffix",
            ToCurrency = "prefix {tag} suffix"
        };
        Assert.Null(props.ExecutionState);

        // Act
        props.UpdateExecutionState(true, new Dictionary<string, string?> { { "tag", "resolved" } });

        // Assert
        Assert.NotNull(props.ExecutionState);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.FromCurrency);
        Assert.Equal("prefix resolved suffix", props.ExecutionState.ToCurrency);
    }
}

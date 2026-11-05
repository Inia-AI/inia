using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using Newtonsoft.Json;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class ExchangeRatePropertiesTests
{
    [Fact]
    public void HasPlaceholder_ContainsInFromCurrency_ReturnsTrue()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "{sourceCurrency}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{sourceCurrency}"));
    }

    [Fact]
    public void HasPlaceholder_ContainsInToCurrency_ReturnsTrue()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            ToCurrency = "{targetCurrency}",
        };

        // Act & Assert
        Assert.True(props.HasPlaceholder("{targetCurrency}"));
    }

    [Fact]
    public void HasPlaceholder_DoesNotContain_ReturnsFalse()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
        };

        // Act & Assert
        Assert.False(props.HasPlaceholder("{missing}"));
    }

    [Fact]
    public void HasPlaceholder_NullProperties_ReturnsFalse()
    {
        // Arrange
        var props = new ExchangeRateProperties();

        // Act & Assert
        Assert.False(props.HasPlaceholder("{anything}"));
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsAllPlaceholders()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "{sourceCurrency}",
            ToCurrency = "{targetCurrency}",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Contains("sourceCurrency", result);
        Assert.Contains("targetCurrency", result);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetAllPlaceholders_ReturnsEmpty_WhenNoPlaceholders()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
        };

        // Act
        var result = props.GetAllPlaceholders().ToList();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void ReplaceAll_ReplacesInAllTemplateProperties()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "Original From",
            ToCurrency = "Original To",
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("Replaced From", props.FromCurrency);
        Assert.Equal("Replaced To", props.ToCurrency);
    }

    [Fact]
    public void ReplaceAll_NullProperties_DoesNotThrow()
    {
        // Arrange
        var props = new ExchangeRateProperties();

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act & Assert (should not throw)
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);
    }

    [Fact]
    public void Serialize_NullToCurrencies_OmitsProperty_BackCompat()
    {
        // Arrange - legacy single-target workflow (ToCurrencies never set)
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
        };

        // Act
        string json = JsonConvert.SerializeObject(props);

        // Assert - no ToCurrencies key emitted, so existing serialized workflows are unchanged
        Assert.DoesNotContain("ToCurrencies", json);
    }

    [Fact]
    public void SerializeDeserialize_WithToCurrencies_RoundTrips()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["GBP", "CHF"],
        };

        // Act
        string json = JsonConvert.SerializeObject(props);
        var restored = JsonConvert.DeserializeObject<ExchangeRateProperties>(json);

        // Assert
        Assert.NotNull(restored?.ToCurrencies);
        Assert.Equal(["GBP", "CHF"], restored.ToCurrencies);
    }

    [Fact]
    public void Deserialize_LegacyJsonWithoutToCurrencies_YieldsNull()
    {
        // Arrange - JSON authored before this feature existed
        const string legacyJson = """{"FromCurrency":"USD","ToCurrency":"EUR","UseCurrentDate":true}""";

        // Act
        var restored = JsonConvert.DeserializeObject<ExchangeRateProperties>(legacyJson);

        // Assert
        Assert.NotNull(restored);
        Assert.Null(restored.ToCurrencies);
    }

    [Fact]
    public void Clone_DuplicatesToCurrencies_Independently()
    {
        // Arrange
        var props = new ExchangeRateProperties
        {
            FromCurrency = "USD",
            ToCurrency = "EUR",
            ToCurrencies = ["GBP", "CHF"],
        };

        // Act
        var clone = (ExchangeRateProperties)props.Clone();
        clone.ToCurrencies!.Add("JPY");

        // Assert - clone has its own list; original is unaffected
        Assert.Equal(["GBP", "CHF"], props.ToCurrencies);
        Assert.Equal(["GBP", "CHF", "JPY"], clone.ToCurrencies);
    }

    [Fact]
    public void ReplaceAll_ReplacesInToCurrenciesItems()
    {
        // Arrange - ReplaceAll does raw-substring replacement (same convention as the
        // FromCurrency/ToCurrency sibling test above); verifies the List<string> is discovered.
        var props = new ExchangeRateProperties
        {
            ToCurrencies = ["Original A", "GBP", "Original B"],
        };

        Dictionary<string, string?> replacements = new()
        {
            ["Original"] = "Replaced",
        };

        // Act
        props.ReplaceAll(replacements, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal(["Replaced A", "GBP", "Replaced B"], props.ToCurrencies);
    }
}

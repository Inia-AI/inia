using IniaAi.Core.Workflows.Helpers;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using NSubstitute;
using System.Globalization;

namespace IniaAi.Core.Tests.Workflows.Helpers;

public class CurrencyConversionHelperTests : IDisposable
{
    private readonly CultureInfo _originalCulture;
    private readonly ILogger _logger = Substitute.For<ILogger>();

    public CurrencyConversionHelperTests()
    {
        _originalCulture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        CultureInfo.CurrentCulture = _originalCulture;
    }

    #region ParseInput Tests

    [Fact]
    public void ParseInput_NullInput_ReturnsFalseWithNulls()
    {
        (bool isJson, decimal? decimalValue, JObject? jsonValue) = CurrencyConversionHelper.ParseInput(null!);

        Assert.False(isJson);
        Assert.Null(decimalValue);
        Assert.Null(jsonValue);
    }

    [Fact]
    public void ParseInput_EmptyString_ReturnsFalseWithNulls()
    {
        (bool isJson, decimal? decimalValue, JObject? jsonValue) = CurrencyConversionHelper.ParseInput("");

        Assert.False(isJson);
        Assert.Null(decimalValue);
        Assert.Null(jsonValue);
    }

    [Fact]
    public void ParseInput_WhitespaceOnly_ReturnsFalseWithNulls()
    {
        (bool isJson, decimal? decimalValue, JObject? jsonValue) = CurrencyConversionHelper.ParseInput("   ");

        Assert.False(isJson);
        Assert.Null(decimalValue);
        Assert.Null(jsonValue);
    }

    [Fact]
    public void ParseInput_ValidDecimal_ReturnsDecimalValue()
    {
        (bool isJson, decimal? decimalValue, JObject? jsonValue) = CurrencyConversionHelper.ParseInput("100.50");

        Assert.False(isJson);
        Assert.Equal(100.50m, decimalValue);
        Assert.Null(jsonValue);
    }

    [Fact]
    public void ParseInput_ValidInteger_ReturnsDecimalValue()
    {
        (bool isJson, decimal? decimalValue, JObject? jsonValue) = CurrencyConversionHelper.ParseInput("500");

        Assert.False(isJson);
        Assert.Equal(500m, decimalValue);
        Assert.Null(jsonValue);
    }

    [Fact]
    public void ParseInput_ValidJsonObject_ReturnsJsonValue()
    {
        string input = """{"currency": "USD", "amount": 100}""";

        (bool isJson, decimal? decimalValue, JObject? jsonValue) = CurrencyConversionHelper.ParseInput(input);

        Assert.True(isJson);
        Assert.Null(decimalValue);
        Assert.NotNull(jsonValue);
        Assert.Equal("USD", jsonValue["currency"]?.ToString());
    }

    [Fact]
    public void ParseInput_InvalidJsonAndInvalidDecimal_ReturnsFalseWithNulls()
    {
        (bool isJson, decimal? decimalValue, JObject? jsonValue) = CurrencyConversionHelper.ParseInput("not-a-number-or-json");

        Assert.False(isJson);
        Assert.Null(decimalValue);
        Assert.Null(jsonValue);
    }

    #endregion

    #region ConvertSingleValue Tests

    [Fact]
    public void ConvertSingleValue_SimpleMultiplication_ReturnsCorrectResult()
    {
        decimal result = CurrencyConversionHelper.ConvertSingleValue(100m, 1.5m);

        Assert.Equal(150m, result);
    }

    [Fact]
    public void ConvertSingleValue_RoundsToTwoDecimals()
    {
        decimal result = CurrencyConversionHelper.ConvertSingleValue(100m, 1.23456m);

        Assert.Equal(123.46m, result);
    }

    [Fact]
    public void ConvertSingleValue_ZeroValue_ReturnsZero()
    {
        decimal result = CurrencyConversionHelper.ConvertSingleValue(0m, 1.5m);

        Assert.Equal(0m, result);
    }

    #endregion

    #region DetectAllCurrencies Tests

    [Fact]
    public void DetectAllCurrencies_SingleCurrency_ReturnsOne()
    {
        var json = JObject.Parse("""{"currency": "USD", "amount": 100}""");

        HashSet<string> currencies = CurrencyConversionHelper.DetectAllCurrencies(json);

        _ = Assert.Single(currencies);
        Assert.Contains("USD", currencies);
    }

    [Fact]
    public void DetectAllCurrencies_MultipleCurrencies_ReturnsAll()
    {
        var json = JObject.Parse("""
        {
            "items": [
                {"currency": "USD", "amount": 100},
                {"currency": "EUR", "amount": 200},
                {"currency": "GBP", "amount": 300}
            ]
        }
        """);

        HashSet<string> currencies = CurrencyConversionHelper.DetectAllCurrencies(json);

        Assert.Equal(3, currencies.Count);
        Assert.Contains("USD", currencies);
        Assert.Contains("EUR", currencies);
        Assert.Contains("GBP", currencies);
    }

    [Fact]
    public void DetectAllCurrencies_NoCurrency_ReturnsEmpty()
    {
        var json = JObject.Parse("""{"amount": 100}""");

        HashSet<string> currencies = CurrencyConversionHelper.DetectAllCurrencies(json);

        Assert.Empty(currencies);
    }

    [Fact]
    public void DetectAllCurrencies_DuplicateCurrencies_ReturnsUnique()
    {
        var json = JObject.Parse("""
        {
            "items": [
                {"currency": "USD", "amount": 100},
                {"currency": "USD", "amount": 200}
            ]
        }
        """);

        HashSet<string> currencies = CurrencyConversionHelper.DetectAllCurrencies(json);

        _ = Assert.Single(currencies);
    }

    #endregion

    #region ConvertJsonCurrency Tests

    [Fact]
    public void ConvertJsonCurrency_ObjectWithCurrency_ConvertsNumbers()
    {
        var json = JObject.Parse("""{"currency": "USD", "amount": 100, "tax": 10}""");
        var rates = new Dictionary<string, decimal> { ["USD"] = 2m };

        CurrencyConversionHelper.ConvertJsonCurrency(json, rates, "EUR", _logger);

        Assert.Equal("EUR", json["currency"]?.ToString());
        Assert.Equal(200m, json["amount"]?.Value<decimal>());
        Assert.Equal(20m, json["tax"]?.Value<decimal>());
    }

    [Fact]
    public void ConvertJsonCurrency_ObjectWithoutCurrency_DoesNotConvertNumbers()
    {
        var json = JObject.Parse("""{"amount": 100, "tax": 10}""");
        var rates = new Dictionary<string, decimal> { ["USD"] = 2m };

        CurrencyConversionHelper.ConvertJsonCurrency(json, rates, "EUR", _logger);

        Assert.Equal(100m, json["amount"]?.Value<decimal>());
        Assert.Equal(10m, json["tax"]?.Value<decimal>());
    }

    [Fact]
    public void ConvertJsonCurrency_MultipleCurrencies_UsesCorrectRateForEach()
    {
        var json = JObject.Parse("""
        {
            "currency": "USD",
            "amount": 100,
            "nested": {
                "currency": "GBP",
                "fee": 50
            }
        }
        """);
        var rates = new Dictionary<string, decimal>
        {
            ["USD"] = 0.85m,  // USD to EUR
            ["GBP"] = 1.17m   // GBP to EUR
        };

        CurrencyConversionHelper.ConvertJsonCurrency(json, rates, "EUR", _logger);

        Assert.Equal("EUR", json["currency"]?.ToString());
        Assert.Equal(85m, json["amount"]?.Value<decimal>());
        Assert.Equal("EUR", json["nested"]?["currency"]?.ToString());
        Assert.Equal(58.5m, json["nested"]?["fee"]?.Value<decimal>());
    }

    [Fact]
    public void ConvertJsonCurrency_NestedObjectWithoutCurrency_DoesNotConvert()
    {
        var json = JObject.Parse("""
        {
            "currency": "USD",
            "amount": 100,
            "nested": {
                "fee": 50
            }
        }
        """);
        var rates = new Dictionary<string, decimal> { ["USD"] = 2m };

        CurrencyConversionHelper.ConvertJsonCurrency(json, rates, "EUR", _logger);

        Assert.Equal("EUR", json["currency"]?.ToString());
        Assert.Equal(200m, json["amount"]?.Value<decimal>());
        Assert.Equal(50m, json["nested"]?["fee"]?.Value<decimal>());
    }

    [Fact]
    public void ConvertJsonCurrency_ArrayOfNumbers_ConvertsAll()
    {
        var json = JObject.Parse("""{"currency": "USD", "values": [10, 20, 30]}""");
        var rates = new Dictionary<string, decimal> { ["USD"] = 2m };

        CurrencyConversionHelper.ConvertJsonCurrency(json, rates, "EUR", _logger);

        var values = json["values"] as JArray;
        Assert.Equal(20m, values?[0]?.Value<decimal>());
        Assert.Equal(40m, values?[1]?.Value<decimal>());
        Assert.Equal(60m, values?[2]?.Value<decimal>());
    }

    [Fact]
    public void ConvertJsonCurrency_ArrayOfObjectsWithDifferentCurrencies_ConvertsEachCorrectly()
    {
        var json = JObject.Parse("""
        {
            "items": [
                {"currency": "USD", "amount": 100},
                {"currency": "GBP", "amount": 200},
                {"amount": 300}
            ]
        }
        """);
        var rates = new Dictionary<string, decimal>
        {
            ["USD"] = 0.85m,
            ["GBP"] = 1.17m
        };

        CurrencyConversionHelper.ConvertJsonCurrency(json, rates, "EUR", _logger);

        var items = json["items"] as JArray;
        Assert.Equal("EUR", items?[0]?["currency"]?.ToString());
        Assert.Equal(85m, items?[0]?["amount"]?.Value<decimal>());
        Assert.Equal("EUR", items?[1]?["currency"]?.ToString());
        Assert.Equal(234m, items?[1]?["amount"]?.Value<decimal>());
        Assert.Equal(300m, items?[2]?["amount"]?.Value<decimal>());
    }

    [Fact]
    public void ConvertJsonCurrency_MissingRate_LogsWarningAndSkips()
    {
        var json = JObject.Parse("""{"currency": "GBP", "amount": 100}""");
        var rates = new Dictionary<string, decimal> { ["USD"] = 2m };

        CurrencyConversionHelper.ConvertJsonCurrency(json, rates, "EUR", _logger);

        Assert.Equal("GBP", json["currency"]?.ToString());
        Assert.Equal(100m, json["amount"]?.Value<decimal>());
        _logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("No rate available")),
            Arg.Any<Exception>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public void ConvertJsonCurrency_CurrencyCaseInsensitive_MatchesAnyCase()
    {
        var json = JObject.Parse("""{"CURRENCY": "usd", "amount": 100}""");
        var rates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 2m };

        CurrencyConversionHelper.ConvertJsonCurrency(json, rates, "EUR", _logger);

        Assert.Equal("EUR", json["CURRENCY"]?.ToString());
        Assert.Equal(200m, json["amount"]?.Value<decimal>());
    }

    [Fact]
    public void ConvertJsonCurrency_StringValue_NotConverted()
    {
        var json = JObject.Parse("""{"currency": "USD", "amount": 100, "date": "2024-01-01"}""");
        var rates = new Dictionary<string, decimal> { ["USD"] = 2m };

        CurrencyConversionHelper.ConvertJsonCurrency(json, rates, "EUR", _logger);

        Assert.Equal("2024-01-01", json["date"]?.ToString());
    }

    #endregion

    #region ConvertJsonToTargets Tests

    [Fact]
    public void ConvertJsonToTargets_ProducesOneConvertedDocPerTarget()
    {
        var source = JObject.Parse("""{"currency": "USD", "amount": 100}""");
        var ratesByTarget = new Dictionary<string, IReadOnlyDictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 0.92m },
            ["GBP"] = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 0.79m },
        };

        Dictionary<string, JObject> result = CurrencyConversionHelper.ConvertJsonToTargets(
            source, ["EUR", "GBP"], ratesByTarget, _logger);

        Assert.Equal(2, result.Count);
        Assert.Equal("EUR", result["EUR"]["currency"]!.ToString());
        Assert.Equal(92.00m, result["EUR"]["amount"]!.Value<decimal>());
        Assert.Equal("GBP", result["GBP"]["currency"]!.ToString());
        Assert.Equal(79.00m, result["GBP"]["amount"]!.Value<decimal>());
    }

    [Fact]
    public void ConvertJsonToTargets_DoesNotMutateSource()
    {
        var source = JObject.Parse("""{"currency": "USD", "amount": 100}""");
        var ratesByTarget = new Dictionary<string, IReadOnlyDictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase) { ["USD"] = 0.92m },
        };

        _ = CurrencyConversionHelper.ConvertJsonToTargets(source, ["EUR"], ratesByTarget, _logger);

        // Source untouched: still USD / 100.
        Assert.Equal("USD", source["currency"]!.ToString());
        Assert.Equal(100m, source["amount"]!.Value<decimal>());
    }

    [Fact]
    public void ConvertJsonToTargets_MultiSource_UsesPerSourceRates()
    {
        var source = JObject.Parse("""
        {
            "items": [
                {"currency": "USD", "amount": 100},
                {"currency": "GBP", "amount": 200}
            ]
        }
        """);
        var ratesByTarget = new Dictionary<string, IReadOnlyDictionary<string, decimal>>(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
            {
                ["USD"] = 0.92m,
                ["GBP"] = 1.17m,
            },
        };

        Dictionary<string, JObject> result = CurrencyConversionHelper.ConvertJsonToTargets(
            source, ["EUR"], ratesByTarget, _logger);

        var items = (JArray)result["EUR"]["items"]!;
        Assert.Equal(92.00m, items[0]!["amount"]!.Value<decimal>());
        Assert.Equal(234.00m, items[1]!["amount"]!.Value<decimal>());
    }

    #endregion
}

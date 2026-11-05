using System.Text.Json.Nodes;
using IniaAi.Core.Services;

namespace IniaAi.Core.Tests.Services;

public class JsonStringReplacerStrategyTests
{
    private static readonly string _extendedJsonData = @"
        {
            ""2020"": {
                ""Income Statement"": {
                    ""Revenue"": { ""Value"": 44947.0, ""Notes"": ""29"" },
                    ""Cost of Sales"": { ""Value"": -33551.0, ""Notes"": ""30"" },
                    ""Gross Profit"": { ""Value"": 11396.0, ""Notes"": """" },
                    ""Tax Expense"": { ""Value"": -2650.0, ""Notes"": """" }
                },
                ""Balance Sheet"": {
                    ""Cash & Equivalents"": { ""Value"": 9413.0, ""Notes"": """" },
                    ""Inventory"": { ""Value"": 3893.0, ""Notes"": """" }
                }
            },
            ""2021"": {
                ""Income Statement"": {
                    ""Revenue"": { ""Value"": 155542.0, ""Notes"": """" },
                    ""Cost of Sales"": { ""Value"": -55549.0, ""Notes"": """" }
                },
                ""Balance Sheet"": {
                    ""Cash & Equivalents"": { ""Value"": 8254.0, ""Notes"": """" }
                }
            },
            ""2022"": {
                ""Income Statement"": {
                    ""Operating Profit"": { ""Value"": 55000.75, ""Notes"": """" },
                    ""SG&A"": { ""Value"": -5000.0, ""Notes"": """" }
                }
            },
            ""2023"": {
                ""Income Statement"": {
                    ""Revenue"": { ""Value"": 300999.99, ""Notes"": """" },
                    ""Cost of Sales"": { ""Value"": -140500.12, ""Notes"": """" },
                    ""Gross Profit"": { ""Value"": 160499.87, ""Notes"": """" },
                    ""Operating Profit"": { ""Value"": 60499.87, ""Notes"": """" }
                }
            },
            ""2024"": {
                ""Income Statement"": {
                    ""Revenue"": { ""Value"": 500000.0, ""Notes"": """" },
                    ""Cost of Sales"": { ""Value"": -250000.0, ""Notes"": """" },
                    ""Gross Profit"": { ""Value"": 250000.0, ""Notes"": """" },
                    ""Operating Profit"": { ""Value"": 100000.0, ""Notes"": """" },
                    ""Depreciation & Amortization"": { ""Value"": -9999.99, ""Notes"": """" }
                }
            }
        }";

    private readonly JsonStringReplacerStrategy _strategy;

    public JsonStringReplacerStrategyTests()
    {
        _strategy = new JsonStringReplacerStrategy();
    }

    [Fact]
    public void ReplacePlaceholders_StandaloneModeWithValidValuePath_ReturnsValue()
    {
        // Arrange
        string path = "2020.Income Statement.Revenue.Value";
        string expected = "44947.0";

        // Act
        string result = _strategy.ReplacePlaceholders(path, _extendedJsonData, embeddedMode: false);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_StandaloneModeWithObjectPath_ReturnsJsonObject()
    {
        // Arrange
        string path = "2020.Income Statement";

        // Act
        string result = _strategy.ReplacePlaceholders(path, _extendedJsonData, false);

        // Assert
        Assert.False(string.IsNullOrEmpty(result));
        Assert.Contains("Revenue", result);
        Assert.Contains("44947", result);
    }

    [Fact]
    public void ReplacePlaceholders_StandaloneModeWithInvalidPath_ReturnsDefaultNoValue()
    {
        // Arrange
        string path = "2020.Income Statement.BadKey.Value";

        // Act
        string result = _strategy.ReplacePlaceholders(path, _extendedJsonData, false);

        // Assert
        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_StandaloneModeWithYearObject_ReturnsJsonObject()
    {
        // Arrange
        string path = "2022";

        // Act
        string result = _strategy.ReplacePlaceholders(path, _extendedJsonData, false);

        // Assert
        Assert.False(string.IsNullOrEmpty(result));
        Assert.Contains("Income Statement", result);
        Assert.Contains("Operating Profit", result);
    }

    [Fact]
    public void ReplacePlaceholders_StandaloneModeWithEmptyJson_ReturnsDefaultNoValue()
    {
        // Arrange
        string path = "2020.Income Statement.Revenue.Value";
        string emptyJson = "";

        // Act
        string result = _strategy.ReplacePlaceholders(path, emptyJson, false);

        // Assert
        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithObjectPath_ReturnsJsonObject()
    {
        // Arrange
        string input = "{2020.Income Statement}";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.False(string.IsNullOrEmpty(result));
        Assert.Contains("Revenue", result);
        Assert.Contains("44947", result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithIncomeStatementObject_ReturnsJsonObject()
    {
        string input = "Something => {2023.Income Statement}";
        string output = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);
        Assert.StartsWith("Something => ", output);
        Assert.Contains("Revenue", output);
        Assert.Contains("300999.99", output); // Revenue value for 2023
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithMixedValidAndObjectPlaceholders_ReturnsBothResults()
    {
        string input = "{2020.Income Statement.Revenue.Value} plus {2020.Balance Sheet}";
        string output = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);
        Assert.StartsWith("44947", output);
        Assert.Contains("plus", output);
        Assert.Contains("Cash", output); // Should contain Balance Sheet data
        Assert.Contains("9413", output); // Cash & Equivalents value for 2020
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithSpacesInPlaceholder_ReturnsValue()
    {
        string input = "Revenue 2021 => {   2021.Income Statement.Revenue.Value   } done.";
        string expected = "Revenue 2021 => 155542.0 done.";
        string output = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);
        Assert.Equal(expected, output);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithNonexistentArray_ReturnsDefaultNoValue()
    {
        string input = "{2024.someArray}";
        string output = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);
        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, output);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithMultiplePlaceholdersObjectAndValue_ReturnsBothResults()
    {
        string input = "Line => {2024.Income Statement}, Val => {2024.Income Statement.Revenue.Value}";
        string output = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);
        Assert.Contains("Line =>", output);
        Assert.Contains("Revenue", output); // Should contain Income Statement JSON
        Assert.Contains("Val => 500000", output); // Should contain the specific value
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithMultiplePlaceholdersSomeInvalid_ReturnsValidAndDefaultForInvalid()
    {
        // Arrange
        string input = "OP 2022: { 2022.Income Statement.Operating Profit.Value }, " +
                       "Bad: { 2022.BadCategory.Unknown.Value }, " +
                       "SG&A: { 2022.Income Statement.SG&A.Value }.";
        string expected = "OP 2022: 55000.75, " +
                          $"Bad: {JsonStringReplacerStrategy.DefaultNoValue}, " +
                          "SG&A: -5000.0.";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithCostOfSalesValue_ReturnsNegativeValue()
    {
        // Arrange
        string input = "Cost is => { 2020.Income Statement.Cost of Sales.Value } <= done.";
        string expected = "Cost is => -33551.0 <= done.";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithNoPlaceholders_ReturnsOriginalString()
    {
        // Arrange
        string input = "Plain text, no braces or placeholders.";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(input, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithSingleValidPlaceholder_ReturnsValue()
    {
        // Arrange
        string input = "{ 2020.Balance Sheet.Inventory.Value }";
        string expected = "3893.0";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithSingleInvalidPlaceholder_ReturnsDefaultNoValue()
    {
        // Arrange
        string input = "{ 2025.Income Statement.Revenue.Value }";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithMultipleValidPlaceholders_ReturnsAllValues()
    {
        // Arrange
        string input = "Rev => {   2020.Income Statement.Revenue.Value} " +
                       "Cash => { 2020.Balance Sheet.Cash & Equivalents.Value }";
        string expected = "Rev => 44947.0 Cash => 9413.0";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithMultiplePlaceholdersPartialInvalid_ReturnsValidAndDefaultForInvalid()
    {
        // Arrange
        string input = "Rev => { 2024.Income Statement.Revenue.Value } " +
                       "Bad => { 2024.Income Statement.BadKey.Value }";
        string expected = $"Rev => 500000.0 Bad => {JsonStringReplacerStrategy.DefaultNoValue}";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithSpacesBetweenPathSegments_ReturnsValue()
    {
        // Arrange
        string input = "{ 2021.   Income Statement   .  Cost of Sales.Value }";
        string expected = "-55549.0";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithNullJson_ReturnsDefaultNoValue()
    {
        // Arrange
        string input = "{ 2021.Income Statement.Revenue.Value } only";
        string expected = $"{JsonStringReplacerStrategy.DefaultNoValue} only";

        // Act
        string result = _strategy.ReplacePlaceholders(input, null, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithBrokenJson_ReturnsDefaultNoValue()
    {
        // Arrange
        string input = "OP => { 2022.Income Statement.Operating Profit.Value } end";
        string brokenJson = "{ invalid: ";
        string expected = $"OP => {JsonStringReplacerStrategy.DefaultNoValue} end";

        // Act
        string result = _strategy.ReplacePlaceholders(input, brokenJson, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithSpacesAroundPlaceholder_ReturnsValue()
    {
        // Arrange
        string input = "Tax => {   2020.Income Statement.Tax Expense.Value     } done";
        string expected = "Tax => -2650.0 done";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithMultiplePlaceholdersMixedValidInvalid_ReturnsValidAndDefaultForInvalid()
    {
        // Arrange
        string input = "A) { 2021.Income Statement.Revenue.Value }, B) { 2021.Balance Sheet.CashX }, C) { 2021.Balance Sheet.Cash & Equivalents.Value}";
        string expected = $"A) 155542.0, B) {JsonStringReplacerStrategy.DefaultNoValue}, C) 8254.0";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithMultipleValidPlaceholdersOnly_ReturnsAllValues()
    {
        // Arrange
        string input = "{ 2020.Income Statement.Revenue.Value } { 2022.Income Statement.Operating Profit.Value }";
        string expected = "44947.0 55000.75";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithMultiplePlaceholdersAndSpaces_ReturnsValidAndDefaultForInvalid()
    {
        // Arrange
        string input = "Line => { 2023.Income Statement.Gross Profit.Value } # " +
                       "{ 2023.Income Statement.Cost of Sales.Value} # " +
                       "{ 2023.Balance Sheet.Inventory.Value }";
        string expected = $"Line => 160499.87 # -140500.12 # {JsonStringReplacerStrategy.DefaultNoValue}";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithMultilineInput_ReturnsValidAndDefaultForInvalid()
    {
        // Arrange
        string input = @"
                2020Revenue => { 2020.Income Statement.Revenue.Value }
                2024Profit => { 2024.Income Statement.Operating Profit.Value }
                Bad => { 2024.Income Statement.BadKey.Value }
                ";
        string expected = @$"
                2020Revenue => 44947.0
                2024Profit => 100000.0
                Bad => {JsonStringReplacerStrategy.DefaultNoValue}
                ";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_StandaloneModeWithNotesPath_ReturnsStringValue()
    {
        // Arrange
        string path = "2020.Income Statement.Revenue.Notes";
        string expected = "29";

        // Act
        string result = _strategy.ReplacePlaceholders(path, _extendedJsonData, false);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithNotesPath_ReturnsStringValue()
    {
        // Arrange
        string input = "Note => { 2020.Income Statement.Cost of Sales.Notes } done.";
        string expected = "Note => 30 done.";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithValueAndNotesPlaceholders_ReturnsBothValues()
    {
        // Arrange
        string input = "GP => { 2020.Income Statement.Gross Profit.Value }, note => { 2020.Income Statement.Gross Profit.Notes } done";
        string expected = "GP => 11396.0, note =>  done";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithEmptyNotesValue_ReturnsEmptyString()
    {
        // Arrange
        string input = "{ 2020.Income Statement.Tax Expense.Notes }";
        string expected = "";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithInvalidNotesPath_ReturnsDefaultNoValue()
    {
        // Arrange
        string input = "Notes => { 2022.Income Statement.SomeBadKey.Notes } done";
        string expected = $"Notes => {JsonStringReplacerStrategy.DefaultNoValue} done";

        // Act
        string result = _strategy.ReplacePlaceholders(input, _extendedJsonData, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_StandaloneModeWithNumericJsonData_ReturnsDefaultNoValue()
    {
        // Arrange - Regression test for TERM-990
        // JsonNode.Parse("9") succeeds but creates a JsonValue, not JsonObject
        // The code should reject primitive JSON values
        string path = "2020.Income Statement.Revenue.Value";
        string numericJson = "9";

        // Act
        string result = _strategy.ReplacePlaceholders(path, numericJson, false);

        // Assert
        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_StandaloneModeWithStringJsonData_ReturnsDefaultNoValue()
    {
        // Arrange - Regression test for TERM-990
        // Primitive string JSON should be rejected
        string path = "some.path";
        string stringJson = "\"hello\"";

        // Act
        string result = _strategy.ReplacePlaceholders(path, stringJson, false);

        // Assert
        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_StandaloneModeWithBooleanJsonData_ReturnsDefaultNoValue()
    {
        // Arrange - Regression test for TERM-990
        // Primitive boolean JSON should be rejected
        string path = "some.path";
        string booleanJson = "true";

        // Act
        string result = _strategy.ReplacePlaceholders(path, booleanJson, false);

        // Assert
        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithNumericJsonData_ReturnsDefaultNoValue()
    {
        // Arrange - Regression test for TERM-990
        // Embedded mode should also reject numeric JSON
        string input = "Value => { 2020.Revenue.Value } done";
        string numericJson = "42";
        string expected = $"Value => {JsonStringReplacerStrategy.DefaultNoValue} done";

        // Act
        string result = _strategy.ReplacePlaceholders(input, numericJson, true);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithStringJsonData_ReturnsDefaultNoValue()
    {
        // Arrange - Regression test for TERM-990
        // Embedded mode should also reject string JSON
        string input = "Text => { some.path } done";
        string stringJson = "\"test\"";
        string expected = $"Text => {JsonStringReplacerStrategy.DefaultNoValue} done";

        // Act
        string result = _strategy.ReplacePlaceholders(input, stringJson, true);

        // Assert
        Assert.Equal(expected, result);
    }

    private const string PERPLEXITY_SHAPED_JSON = """
        {
            "id": "resp-1",
            "choices": [
                {
                    "index": 0,
                    "message": { "role": "assistant", "content": "Hello world" }
                },
                {
                    "index": 1,
                    "message": { "role": "assistant", "content": "Second reply" }
                }
            ],
            "matrix": [[1, 2, 3], [4, 5, 6]],
            "tags": ["alpha", "beta"]
        }
        """;

    [Fact]
    public void ReplacePlaceholders_ArrayIndex_ReturnsValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "choices[0].message.content",
            PERPLEXITY_SHAPED_JSON,
            embeddedMode: false);

        Assert.Equal("Hello world", result);
    }

    [Fact]
    public void ReplacePlaceholders_ArrayIndexNonZero_ReturnsValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "choices[1].message.content",
            PERPLEXITY_SHAPED_JSON,
            embeddedMode: false);

        Assert.Equal("Second reply", result);
    }

    [Fact]
    public void ReplacePlaceholders_ChainedArrayIndices_ReturnsValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "matrix[1][2]",
            PERPLEXITY_SHAPED_JSON,
            embeddedMode: false);

        Assert.Equal("6", result);
    }

    [Fact]
    public void ReplacePlaceholders_ArrayIndexOutOfBounds_ReturnsDefaultNoValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "choices[99].message.content",
            PERPLEXITY_SHAPED_JSON,
            embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_ArrayIndexOnNonArray_ReturnsDefaultNoValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "id[0]",
            PERPLEXITY_SHAPED_JSON,
            embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_BareBracketSegment_ReturnsDefaultNoValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "choices.[0].message.content",
            PERPLEXITY_SHAPED_JSON,
            embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_JsonArrayLeaf_ReturnsSerializedArray()
    {
        string result = _strategy.ReplacePlaceholders(
            "tags",
            PERPLEXITY_SHAPED_JSON,
            embeddedMode: false);

        Assert.Contains("alpha", result);
        Assert.Contains("beta", result);
    }

    [Fact]
    public void ReplacePlaceholders_NonNumericArrayIndex_ReturnsDefaultNoValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "choices[abc].message.content",
            PERPLEXITY_SHAPED_JSON,
            embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmptyArrayIndex_ReturnsDefaultNoValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "choices[].message.content",
            PERPLEXITY_SHAPED_JSON,
            embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_ArrayIndexOverflow_ReturnsDefaultNoValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "choices[99999999999999999999].message.content",
            PERPLEXITY_SHAPED_JSON,
            embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedModeWithArrayIndex_ReturnsValue()
    {
        string input = "Answer: {choices[0].message.content}!";
        string expected = "Answer: Hello world!";

        string result = _strategy.ReplacePlaceholders(input, PERPLEXITY_SHAPED_JSON, embeddedMode: true);

        Assert.Equal(expected, result);
    }

    // Top-level array roots (e.g. Financial Modeling Prep "[ { ... } ]" responses).
    private const string FMP_SINGLE_ELEMENT_ARRAY = """
        [
            {
                "companyName": "Acme Capital Inc.",
                "description": "A global financial services provider.",
                "quote": { "price": 135.42, "currency": "USD" }
            }
        ]
        """;

    private const string FMP_MULTI_ELEMENT_ARRAY = """
        [
            { "date": "2025-09-30", "netIncome": 100 },
            { "date": "2024-09-30", "netIncome": 200 }
        ]
        """;

    [Fact]
    public void ReplacePlaceholders_SingleElementArrayRoot_AutoUnwrapsToField()
    {
        string result = _strategy.ReplacePlaceholders(
            "companyName",
            FMP_SINGLE_ELEMENT_ARRAY,
            embeddedMode: false);

        Assert.Equal("Acme Capital Inc.", result);
    }

    [Fact]
    public void ReplacePlaceholders_SingleElementArrayRoot_AutoUnwrapsToNestedField()
    {
        string result = _strategy.ReplacePlaceholders(
            "quote.currency",
            FMP_SINGLE_ELEMENT_ARRAY,
            embeddedMode: false);

        Assert.Equal("USD", result);
    }

    [Fact]
    public void ReplacePlaceholders_SingleElementArrayRoot_EmbeddedMode_ReturnsValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "Company: {companyName}",
            FMP_SINGLE_ELEMENT_ARRAY,
            embeddedMode: true);

        Assert.Equal("Company: Acme Capital Inc.", result);
    }

    [Fact]
    public void ReplacePlaceholders_LeadingIndexOnArrayRoot_ReturnsValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "[0].netIncome",
            FMP_MULTI_ELEMENT_ARRAY,
            embeddedMode: false);

        Assert.Equal("100", result);
    }

    [Fact]
    public void ReplacePlaceholders_LeadingIndexNonZeroOnArrayRoot_ReturnsValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "[1].netIncome",
            FMP_MULTI_ELEMENT_ARRAY,
            embeddedMode: false);

        Assert.Equal("200", result);
    }

    [Fact]
    public void ReplacePlaceholders_LeadingIndexOnlyOnArrayRoot_ReturnsObject()
    {
        string result = _strategy.ReplacePlaceholders(
            "[0]",
            FMP_MULTI_ELEMENT_ARRAY,
            embeddedMode: false);

        Assert.Contains("netIncome", result);
        Assert.Contains("100", result);
    }

    [Fact]
    public void ReplacePlaceholders_MultiElementArrayRootWithoutIndex_ReturnsDefaultNoValue()
    {
        // Ambiguous: which element? Caller must index or constrain the source to one element.
        string result = _strategy.ReplacePlaceholders(
            "netIncome",
            FMP_MULTI_ELEMENT_ARRAY,
            embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_LeadingIndexOutOfBoundsOnArrayRoot_ReturnsDefaultNoValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "[5].netIncome",
            FMP_MULTI_ELEMENT_ARRAY,
            embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedMode_LeadingIndexOnArrayRoot_ReturnsValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "Net: {[0].netIncome}",
            FMP_MULTI_ELEMENT_ARRAY,
            embeddedMode: true);

        Assert.Equal("Net: 100", result);
    }

    [Fact]
    public void ReplacePlaceholders_EmbeddedMode_ArrayRootCompositeAmbiguityAndOutOfBounds_IsolatesPlaceholders()
    {
        string result = _strategy.ReplacePlaceholders(
            "Good {[0].netIncome}, bad {[5].netIncome}",
            FMP_MULTI_ELEMENT_ARRAY,
            embeddedMode: true);

        Assert.Equal($"Good 100, bad {JsonStringReplacerStrategy.DefaultNoValue}", result);
    }

    [Fact]
    public void ReplacePlaceholders_LeadingBareBracketOnObjectRoot_ReturnsDefaultNoValue()
    {
        // Leading-index navigation must stay array-root-only; an object root rejects it.
        string result = _strategy.ReplacePlaceholders(
            "[0].choices",
            "{\"id\":\"resp-1\",\"choices\":[{\"index\":0}]}",
            embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_EmptyArrayRoot_ReturnsDefaultNoValue()
    {
        string result = _strategy.ReplacePlaceholders("companyName", "[]", embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_DoubleWrappedSingleElementArray_DoesNotRecurse_ReturnsDefaultNoValue()
    {
        // Auto-unwrap is one level only: the unwrapped element is itself an array, not an object.
        string result = _strategy.ReplacePlaceholders("x", "[[{\"x\": 1}]]", embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_ChainedLeadingIndexOnArrayOfArraysRoot_ReturnsValue()
    {
        string result = _strategy.ReplacePlaceholders("[1][2]", "[[1,2,3],[4,5,6]]", embeddedMode: false);

        Assert.Equal("6", result);
    }

    [Fact]
    public void ReplacePlaceholders_ChainedLeadingIndexSecondIndexOnNonArray_ReturnsDefaultNoValue()
    {
        // First index lands on an object element; the second index has no array to descend into.
        string result = _strategy.ReplacePlaceholders("[0][0]", FMP_MULTI_ELEMENT_ARRAY, embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_LeadingIndexToArrayElement_SerializesArray()
    {
        string result = _strategy.ReplacePlaceholders("[0]", "[[1,2,3],[4,5,6]]", embeddedMode: false);

        Assert.Equal("[1,2,3]", result);
    }

    [Fact]
    public void ReplacePlaceholders_SingleElementArrayPrimitiveLeadingIndex_ReturnsValue()
    {
        string result = _strategy.ReplacePlaceholders("[0]", "[42]", embeddedMode: false);

        Assert.Equal("42", result);
    }

    [Fact]
    public void ReplacePlaceholders_SingleElementArrayNullLeadingIndex_ReturnsDefaultNoValue()
    {
        string result = _strategy.ReplacePlaceholders("[0]", "[null]", embeddedMode: false);

        Assert.Equal(JsonStringReplacerStrategy.DefaultNoValue, result);
    }

    [Fact]
    public void ReplacePlaceholders_LeadingIndexWinsOverAutoUnwrapOnSingleElement_ReturnsValue()
    {
        // A single-element array with an explicit leading index uses the index branch, not auto-unwrap.
        string result = _strategy.ReplacePlaceholders(
            "[0].companyName",
            "[{\"companyName\":\"Acme Capital Inc.\"}]",
            embeddedMode: false);

        Assert.Equal("Acme Capital Inc.", result);
    }

    [Fact]
    public void ReplacePlaceholders_AutoUnwrapThenIndexIntoArrayField_ReturnsValue()
    {
        string result = _strategy.ReplacePlaceholders(
            "items[1]",
            "[{\"items\":[\"a\",\"b\",\"c\"]}]",
            embeddedMode: false);

        Assert.Equal("b", result);
    }

    [Fact]
    public void ReplacePlaceholders_CaseInsensitiveKey_ResolvesWhenNoExactMatch()
    {
        // Stored keys are "Income Statement" / "Revenue" / "Value"; the path uses other casing.
        string result = _strategy.ReplacePlaceholders(
            "2020.income statement.revenue.VALUE",
            _extendedJsonData,
            embeddedMode: false);

        Assert.Equal("44947.0", result);
    }

    [Fact]
    public void ReplacePlaceholders_ExactCaseWins_WhenBothCasingsPresent()
    {
        // Both "Value" and "value" exist; an exact-case path resolves to the exact match.
        string json = "{\"Value\":\"exact\",\"value\":\"other\"}";

        string result = _strategy.ReplacePlaceholders("Value", json, embeddedMode: false);

        Assert.Equal("exact", result);
    }

    [Fact]
    public void ReplacePlaceholders_ObjectAndArrayValues_KeepArabicAndCyrillicLettersRaw()
    {
        string json = "{\"rows\":[{\"memo\":\"حوالة واردة\",\"name\":\"Иван Петров\"}],\"meta\":{\"bank\":\"البنك\"}}";

        string array = _strategy.ReplacePlaceholders("rows", json, embeddedMode: false);
        string obj = _strategy.ReplacePlaceholders("meta", json, embeddedMode: false);

        Assert.Equal("[{\"memo\":\"حوالة واردة\",\"name\":\"Иван Петров\"}]", array);
        Assert.Equal("{\"bank\":\"البنك\"}", obj);
        Assert.DoesNotContain("\\u", array, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacePlaceholders_ObjectValues_KeepHtmlSensitiveCharactersEscaped()
    {
        string json = "{\"meta\":{\"note\":\"<b>Smith & Sons</b> isn't\"}}";

        string obj = _strategy.ReplacePlaceholders("meta", json, embeddedMode: false);

        Assert.DoesNotContain("<", obj, StringComparison.Ordinal);
        Assert.DoesNotContain(">", obj, StringComparison.Ordinal);
        Assert.DoesNotContain("&", obj, StringComparison.Ordinal);
        Assert.DoesNotContain("'", obj, StringComparison.Ordinal);
        Assert.Equal("<b>Smith & Sons</b> isn't", JsonNode.Parse(obj)!["note"]!.GetValue<string>());
    }

    [Fact]
    public void ReplacePlaceholders_RenderedObject_RoundTripsToTheSameJson()
    {
        string json = "{\"meta\":{\"memo\":\"إيداع نقدي\",\"amount\":1250.10,\"tags\":[\"a\",\"<x>\"],\"nested\":{\"ok\":true}}}";

        string obj = _strategy.ReplacePlaceholders("meta", json, embeddedMode: false);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json)!["meta"], JsonNode.Parse(obj)));
        Assert.Contains("1250.10", obj, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacePlaceholders_LeafStringValues_AreReturnedAsWritten()
    {
        string json = "{\"meta\":{\"memo\":\"حوالة <واردة> & ok\"}}";

        string leaf = _strategy.ReplacePlaceholders("meta.memo", json, embeddedMode: false);

        Assert.Equal("حوالة <واردة> & ok", leaf);
    }
}

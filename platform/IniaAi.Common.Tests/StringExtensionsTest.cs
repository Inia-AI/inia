using IniaAi.Common.Constants;

namespace IniaAi.Common.Tests;

public class StringExtensionsTest
{
    [Fact]
    public void SplitCamelCase_ShouldSplitCorrectly()
    {
        // Arrange
        string input = "CamelCaseString";
        List<string> expected = ["Camel", "Case", "String"];

        // Act
        string[] result = [.. input.SplitCamelCase()];

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetPlaceholders_ShouldExtractPlaceholders()
    {
        // Arrange
        string input = "Hello {name}, welcome to {place ,}!";
        List<string> expected = ["name"];

        // Act
        string[] result = [.. input.GetPlaceholders()];

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetPlaceholders_WithJson_ShouldExtractPlaceholders()
    {
        // Arrange
        string input =
        """
        {
            "level1": {
                "level2": {
                    "level3": "{block_0.answer}"
                }
            }
        }
        """;
        List<string> expected = ["block_0.answer"];

        // Act
        string[] result = [.. input.GetPlaceholders()];

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetPlaceholders_WithNoPlaceholders_ShouldReturnEmptyList()
    {
        // Arrange
        string input = "Hello, welcome!";

        // Act
        string[] result = [.. input.GetPlaceholders()];

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetPlaceholders_WhenStringIsNull_ShouldReturnEmptyList()
    {
        // Arrange
        string? input = null;

        // Act
        string[] result = [.. input.GetPlaceholders()];

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetPlaceholders_ShouldMatch_AlphanumericAndSymbols()
    {
        // Arrange
        string input = "Hello {User_123.Name-Part}!";
        List<string> expected = ["User_123.Name-Part"];

        // Act
        string[] result = [.. input.GetPlaceholders()];

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetPlaceholders_ShouldNotMatch_InvalidCharacters()
    {
        // Arrange
        string input = "Bad: {Invalid, Placeholder} and {another!bad} and {wrong@value}";

        // Act
        string[] result = [.. input.GetPlaceholders()];

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetPlaceholders_ShouldOnlyMatchValidOnes()
    {
        // Arrange
        string input = "{valid_one} and {Invalid, Placeholder} and {Another-One}";
        List<string> expected = ["valid_one", "Another-One"];

        // Act
        string[] result = [.. input.GetPlaceholders()];

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetPlaceholders_ShouldMatchAtStartAndEnd()
    {
        // Arrange
        string input = "{StartPlaceholder} in the middle {middle_one} and at the end {End123}";
        List<string> expected = ["StartPlaceholder", "middle_one", "End123"];

        // Act
        string[] result = [.. input.GetPlaceholders()];

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetPlaceholders_ShouldMatch_NumbersDotsUnderscores()
    {
        // Arrange
        string input = "{12345} {with.dots} {with_underscores}";
        List<string> expected = ["12345", "with.dots", "with_underscores"];

        // Act
        string[] result = [.. input.GetPlaceholders()];

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetPlaceholders_ShouldMatch_PlaceholderWithIndexing()
    {
        // Arrange
        string input = "Hello {counterparty[1].Name}, balance is {accounts[2].Amount}";
        List<string> expected = ["counterparty[1].Name", "accounts[2].Amount"];

        // Act
        string[] result = [.. input.GetPlaceholders()];

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetSha256Hash_ShouldReturnCorrectHash()
    {
        // Arrange
        string input = "test";
        string expected = "9F86D081884C7D659A2FEAA0C55AD015A3BF4F1B2B0B822CD15D6C15B0F00A08";

        // Act
        string result = input.GetSha256Hash();

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void IsValidJson_ReturnsFalseWhenOneWord()
    {
        // Arrange
        string input1 = $"{{{SharedInputKeys.INPUT_CONTEXT}}}";
        string input2 = $"{{{SharedInputKeys.INPUT_CONTEXT}}} TEST string";

        // Act
        bool? result1 = input1.IsValidJson();
        bool? result2 = input2.IsValidJson();

        // Assert
        Assert.Equal(false, result1);
        Assert.Equal(false, result2);
    }

    [Fact]
    public void IsValidJson_ReturnsTrueForTopLevelArray()
    {
        // A top-level JSON array (e.g. a Financial Modeling Prep "[ { ... } ]" response)
        // must be treated as valid JSON so the placeholder resolver routes it to the JSON
        // strategy rather than falling back to plain-string handling.
        string input = "[ { \"companyName\": \"Acme Capital Inc.\" } ]";

        bool? result = input.IsValidJson();

        Assert.Equal(true, result);
    }

    [Fact]
    public void IsValidJson_ReturnsFalseForTextWithAHalfOfASurrogatePair()
    {
        string cutThroughAnEmoji = "{\"label\":\"😀 ok\"}"[..11] + "\"}";

        bool? result = cutThroughAnEmoji.IsValidJson();

        Assert.Equal(false, result);
    }

    [Fact]
    public void IsInvalidBlockProperty_WhenStringIsNull_ShouldReturnTrue()
    {
        // Arrange
        string? input = null;
        string? noDataText = "No Data";

        // Act
        bool result = input.IsInvalidBlockProperty(noDataText);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInvalidBlockProperty_WhenStringIsEmpty_ShouldReturnTrue()
    {
        // Arrange
        string input = "";
        string? noDataText = "No Data";

        // Act
        bool result = input.IsInvalidBlockProperty(noDataText);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInvalidBlockProperty_WhenStringIsWhitespace_ShouldReturnTrue()
    {
        // Arrange
        string input = "   ";
        string? noDataText = "No Data";

        // Act
        bool result = input.IsInvalidBlockProperty(noDataText);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInvalidBlockProperty_WhenStringMatchesNoDataText_ShouldReturnTrue()
    {
        // Arrange
        string input = "No Data";
        string noDataText = "No Data";

        // Act
        bool result = input.IsInvalidBlockProperty(noDataText);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInvalidBlockProperty_WhenStringContainsPlaceholder_ShouldReturnTrue()
    {
        // Arrange
        string input = "{placeholder}";
        string? noDataText = "No Data";

        // Act
        bool result = input.IsInvalidBlockProperty(noDataText);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInvalidBlockProperty_WhenStringContainsComplexPlaceholder_ShouldReturnTrue()
    {
        // Arrange
        string input = "{user[0].name}";
        string? noDataText = "No Data";

        // Act
        bool result = input.IsInvalidBlockProperty(noDataText);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInvalidBlockProperty_WhenNoDataTextIsNull_AndStringIsValid_ShouldReturnFalse()
    {
        // Arrange
        string input = "Valid data";
        string? noDataText = null;

        // Act
        bool result = input.IsInvalidBlockProperty(noDataText);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsInvalidBlockProperty_WhenValidJson_ShouldReturnFalse()
    {
        // Arrange
        string input = "{\"name\": \"value\"}";
        string? noDataText = "No Data";

        // Act
        bool result = input.IsInvalidBlockProperty(noDataText);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsInvalidBlockProperty_WhenValidString_ShouldReturnFalse()
    {
        // Arrange
        string input = "This is valid data";
        string? noDataText = "No Data";

        // Act
        bool result = input.IsInvalidBlockProperty(noDataText);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsInvalidBlockProperty_WhenStringWithWhitespaceMatchesNoDataText_ShouldReturnTrue()
    {
        // Arrange
        string input = "  No Data  ";
        string noDataText = "No Data";

        // Act
        bool result = input.IsInvalidBlockProperty(noDataText);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ReplacePlaceholdersWithUrlEncodedValues_EncodesReservedCharactersInValue()
    {
        // Arrange
        string input = "https://api.example.com/search?q={name}&key=abc";
        var inputs = new Dictionary<string, string?> { { "name", "A & B/C ?D#E=F+G%H" } };

        // Act
        string? result = input.ReplacePlaceholdersWithUrlEncodedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert — value fully encoded, the template's ?, & and = untouched
        Assert.Equal(
            "https://api.example.com/search?q=A%20%26%20B%2FC%20%3FD%23E%3DF%2BG%25H&key=abc",
            result);
    }

    [Fact]
    public void ReplacePlaceholdersWithUrlEncodedValues_DoubleBracedPlaceholder_Encodes()
    {
        // Arrange
        string input = "https://api.example.com/{{id}}/details";
        var inputs = new Dictionary<string, string?> { { "id", "a b" } };

        // Act
        string? result = input.ReplacePlaceholdersWithUrlEncodedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("https://api.example.com/a%20b/details", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithUrlEncodedValues_UnicodeValue_PercentEncodesUtf8()
    {
        // Arrange
        string input = "https://api.example.com/?q={name}";
        var inputs = new Dictionary<string, string?> { { "name", "Müller" } };

        // Act
        string? result = input.ReplacePlaceholdersWithUrlEncodedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("https://api.example.com/?q=M%C3%BCller", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithUrlEncodedValues_NullValue_ReplacesWithEmpty()
    {
        // Arrange
        string input = "https://api.example.com/?q={name}";
        var inputs = new Dictionary<string, string?> { { "name", null } };

        // Act
        string? result = input.ReplacePlaceholdersWithUrlEncodedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("https://api.example.com/?q=", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithUrlEncodedValues_WholeStringIsSinglePlaceholder_SubstitutesVerbatim()
    {
        // Arrange
        string input = "{NextPage.url}";
        var inputs = new Dictionary<string, string?> { { "NextPage.url", "https://x.com/a?b=1&c=2" } };

        // Act
        string? result = input.ReplacePlaceholdersWithUrlEncodedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("https://x.com/a?b=1&c=2", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithUrlEncodedValues_WholeStringIsDoubleBracedPlaceholder_SubstitutesVerbatim()
    {
        // Arrange
        string input = "{{NextPage.url}}";
        var inputs = new Dictionary<string, string?> { { "NextPage.url", "https://x.com/a?b=1&c=2" } };

        // Act
        string? result = input.ReplacePlaceholdersWithUrlEncodedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("https://x.com/a?b=1&c=2", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithUrlEncodedValues_TwoConcatenatedPlaceholders_BothEncoded()
    {
        // Arrange
        string input = "{a}{b}";
        var inputs = new Dictionary<string, string?> { { "a", "x y" }, { "b", "p&q" } };

        // Act
        string? result = input.ReplacePlaceholdersWithUrlEncodedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("x%20yp%26q", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithUrlEncodedValues_RawMode_EncodesValue()
    {
        // Arrange
        string input = "https://api.example.com/?q={name}";
        var inputs = new Dictionary<string, string?> { { "{name}", "a b" } };

        // Act
        string? result = input.ReplacePlaceholdersWithUrlEncodedValues(
            false, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("https://api.example.com/?q=a%20b", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithUrlEncodedValues_NullString_ReturnsNull()
    {
        // Arrange
        string? input = null;

        // Act
        string? result = input.ReplacePlaceholdersWithUrlEncodedValues(
            true, new Dictionary<string, string?> { { "a", "b" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_EscapesQuotesNewlinesBackslashes_TemplateLiteralsUntouched()
    {
        // Arrange
        string input = "{\"query\": \"{q}\", \"limit\": 5}";
        var inputs = new Dictionary<string, string?> { { "q", "say \"hi\"\nC:\\path" } };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert — value escaped, the template's own quotes/braces untouched
        Assert.Equal("{\"query\": \"say \\\"hi\\\"\\nC:\\\\path\", \"limit\": 5}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_DoubleBracedPlaceholder_Escapes()
    {
        // Arrange
        string input = "{\"content\": \"{{msg}}\"}";
        var inputs = new Dictionary<string, string?> { { "msg", "line1\nline2" } };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"content\": \"line1\\nline2\"}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_TabCarriageReturnAndControlChar_Escaped()
    {
        // Arrange
        string input = "{\"v\": \"{q}\"}";
        var inputs = new Dictionary<string, string?> { { "q", "a\tb\rc\u0001d" } };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"v\": \"a\\tb\\rc\\u0001d\"}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_WholeStringIsSinglePlaceholder_SubstitutesVerbatim()
    {
        // Arrange
        string input = "{Payload.json}";
        var inputs = new Dictionary<string, string?> { { "Payload.json", "{\"a\": 1}" } };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert — the value IS the JSON document; no escaping
        Assert.Equal("{\"a\": 1}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_WholeStringIsDoubleBracedPlaceholder_SubstitutesVerbatim()
    {
        // Arrange
        string input = "{{Payload.json}}";
        var inputs = new Dictionary<string, string?> { { "Payload.json", "{\"a\": 1}" } };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"a\": 1}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_PreEscapedValue_SubstitutedVerbatim_AndReported()
    {
        // Arrange — value contains only escape sequences, no raw specials (escape-block output shape)
        string input = "{\"content\": \"{msg}\"}";
        var inputs = new Dictionary<string, string?> { { "msg", "pre \\n escaped \\\"quoted\\\"" } };
        List<string> skipped = [];

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase, skipped.Add);

        // Assert
        Assert.Equal("{\"content\": \"pre \\n escaped \\\"quoted\\\"\"}", result);
        Assert.Equal(["msg"], skipped);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_RawValue_EscapedWithoutReporting()
    {
        // Arrange
        string input = "{\"content\": \"{msg}\"}";
        var inputs = new Dictionary<string, string?> { { "msg", "plain text" } };
        List<string> skipped = [];

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase, skipped.Add);

        // Assert
        Assert.Equal("{\"content\": \"plain text\"}", result);
        Assert.Empty(skipped);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_BackslashFollowedByInvalidEscapeLetter_EscapedNotTreatedAsPreEscaped()
    {
        // Arrange — \U is not a valid JSON escape, so the heuristic cannot read this as pre-escaped
        string input = "{\"path\": \"{p}\"}";
        var inputs = new Dictionary<string, string?> { { "p", "C:\\Users\\temp" } };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"path\": \"C:\\\\Users\\\\temp\"}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_BackslashFormingValidJsonEscape_TreatedAsPreEscapedAndReported()
    {
        // Arrange — \t IS a valid JSON escape, so the heuristic reads C:\temp as pre-escaped and
        // substitutes it verbatim (a server decodes it as "C:<tab>emp"); the skip must be reported.
        string input = "{\"path\": \"{p}\"}";
        var inputs = new Dictionary<string, string?> { { "p", "C:\\temp" } };
        List<string> skipped = [];

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase, skipped.Add);

        // Assert
        Assert.Equal("{\"path\": \"C:\\temp\"}", result);
        Assert.Equal(["p"], skipped);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_NullValue_ReplacesWithEmpty()
    {
        // Arrange
        string input = "{\"q\": \"{name}\"}";
        var inputs = new Dictionary<string, string?> { { "name", null } };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"q\": \"\"}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_RawMode_EscapesValue()
    {
        // Arrange
        string input = "{\"q\": \"{name}\"}";
        var inputs = new Dictionary<string, string?> { { "{name}", "a \"b\"" } };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            false, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"q\": \"a \\\"b\\\"\"}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_NullString_ReturnsNull()
    {
        // Arrange
        string? input = null;

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, new Dictionary<string, string?> { { "a", "b" } },
            StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_BareValuePosition_NestsJsonObjectVerbatim()
    {
        // Arrange — a whole upstream JSON object nested as a value (MergeJson "{"Peer1": {tag}}" shape)
        string input = "{\"Peer1\": {peer.analysis}}";
        var inputs = new Dictionary<string, string?>
        {
            { "peer.analysis", "{\"rating\":\"A\",\"notes\":\"Strong \\\"buy\\\"\"}" },
        };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert — the object is preserved verbatim; the result is still valid JSON
        Assert.Equal("{\"Peer1\": {\"rating\":\"A\",\"notes\":\"Strong \\\"buy\\\"\"}}", result);
        Assert.True(result!.IsValidJson());
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_SameTagInStringAndBarePositions_EscapesOnlyTheInStringOne()
    {
        // Arrange — one occurrence inside a string, one in bare value position
        string input = "{\"label\": \"{v}\", \"data\": {v}}";
        var inputs = new Dictionary<string, string?> { { "v", "{\"n\":1}" } };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert — in-string occurrence escaped, bare occurrence verbatim
        Assert.Equal("{\"label\": \"{\\\"n\\\":1}\", \"data\": {\"n\":1}}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_BareValuePositionWithSpecials_StillVerbatim()
    {
        // Arrange — bare value carrying a newline inside its own (already-valid) JSON must not be escaped
        string input = "[{item}]";
        var inputs = new Dictionary<string, string?> { { "item", "{\"a\":\"x\\ny\"}" } };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("[{\"a\":\"x\\ny\"}]", result);
    }

    [Fact]
    public void RemoveNullCharacters_StripsAllNulCharacters()
    {
        string input = "a" + '\0' + "b" + '\0' + '\0' + "c";

        Assert.Equal("abc", input.RemoveNullCharacters());
    }

    [Fact]
    public void RemoveNullCharacters_CleanString_ReturnsSameInstance()
    {
        string input = "no nul here";

        Assert.Same(input, input.RemoveNullCharacters());
    }

    [Fact]
    public void RemoveNullCharacters_Null_ReturnsNull()
    {
        string? input = null;

        Assert.Null(input.RemoveNullCharacters());
    }

    [Fact]
    public void RemoveNullCharacters_OnlyNulCharacters_ReturnsEmpty()
    {
        string input = new('\0', 3);

        Assert.Equal(string.Empty, input.RemoveNullCharacters());
    }

    [Fact]
    public void AsCopyName_AddsTheCopySuffix_AndShortensTheNameToFit()
    {
        Assert.Equal("Page overview (copy)", "Page overview".AsCopyName(200));
        Assert.Equal("Page (copy)", "Page overview".AsCopyName(12));
    }

    [Fact]
    public void Truncate_CutInsideASurrogatePair_KeepsTheCharacterWhole()
    {
        string text = "ab\U0001F600";

        Assert.Equal("ab", text.Truncate(3));
        Assert.Equal(text, text.Truncate(4));
    }
}

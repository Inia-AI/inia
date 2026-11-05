namespace IniaAi.Common.Tests;

/// <summary>
/// A text input's value is itself a template: authors write "Research {Counterparty.Name} …" for
/// readability, and the engine adds the nested tag to the same inputs dictionary (after the text
/// inputs) so a second replacement pass resolves it. Any replacement variant used on a block
/// property must therefore keep resolving placeholders that arrive inside a substituted value.
/// </summary>
public class NestedPlaceholderChainTests
{
    private static Dictionary<string, string?> TextInputThenNestedTag()
    {
        return new Dictionary<string, string?>
        {
            { "ResearchPrompt", "Research {Counterparty.Name} financials" },
            { "Counterparty.Name", "Acme Corp" },
        };
    }

    [Fact]
    public void ReplacePlaceholders_ValueCarriesNestedTagAddedLater_ResolvesNestedTag()
    {
        // Arrange
        string input = "{\"input\": \"{ResearchPrompt}\"}";

        // Act
        string? result = input.ReplacePlaceholders(
            true, TextInputThenNestedTag(), StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"input\": \"Research Acme Corp financials\"}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_ValueCarriesNestedTagAddedLater_ResolvesNestedTag()
    {
        // Arrange
        string input = "{\"input\": \"{ResearchPrompt}\"}";

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, TextInputThenNestedTag(), StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"input\": \"Research Acme Corp financials\"}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithUrlEncodedValues_ValueCarriesNestedTagAddedLater_ResolvesNestedTagThenEncodesOnce()
    {
        // Arrange
        string input = "https://api.example.com/search?q={ResearchPrompt}";

        // Act
        string? result = input.ReplacePlaceholdersWithUrlEncodedValues(
            true, TextInputThenNestedTag(), StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("https://api.example.com/search?q=Research%20Acme%20Corp%20financials", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_BareValueCarriesNestedTag_ResolvesAndNestsVerbatim()
    {
        // Arrange
        string input = "{\"k\": {Wrapper}}";
        var inputs = new Dictionary<string, string?>
        {
            { "Wrapper", "{Nested.Json}" },
            { "Nested.Json", "{\"a\": 1}" },
        };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"k\": {\"a\": 1}}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_NestedTagValueHasSpecials_ComposedValueEscaped()
    {
        // Arrange
        string input = "{\"input\": \"{ResearchPrompt}\"}";
        var inputs = new Dictionary<string, string?>
        {
            { "ResearchPrompt", "About {Counterparty.Name}." },
            { "Counterparty.Name", "Acme \"AC\" Corp" },
        };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"input\": \"About Acme \\\"AC\\\" Corp.\"}", result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_SameTagTwice_BothOccurrencesResolveIdentically()
    {
        // Arrange
        string input = "{\"a\": \"{ResearchPrompt}\", \"b\": \"{ResearchPrompt}\"}";

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, TextInputThenNestedTag(), StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal(
            "{\"a\": \"Research Acme Corp financials\", \"b\": \"Research Acme Corp financials\"}",
            result);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_NestedResolutionYieldsPreEscapedContent_SkipsEscapingAndReportsTag()
    {
        // Arrange
        string input = "{\"msg\": \"{PreEscaped}\"}";
        var inputs = new Dictionary<string, string?>
        {
            { "PreEscaped", "{EscapedPayload}" },
            { "EscapedPayload", "Say \\\"hi\\\"" },
        };
        var skipped = new List<string>();

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase, skipped.Add);

        // Assert
        Assert.Equal("{\"msg\": \"Say \\\"hi\\\"\"}", result);
        string reportedTag = Assert.Single(skipped);
        Assert.Equal("PreEscaped", reportedTag);
    }

    [Fact]
    public void ReplacePlaceholdersWithJsonEscapedValues_TwoHopForwardChain_FullyResolves()
    {
        // Arrange
        string input = "{\"v\": \"{Outer}\"}";
        var inputs = new Dictionary<string, string?>
        {
            { "Outer", "A {Middle} B" },
            { "Middle", "C {Counterparty.Name} D" },
            { "Counterparty.Name", "Acme" },
        };

        // Act
        string? result = input.ReplacePlaceholdersWithJsonEscapedValues(
            true, inputs, StringComparison.InvariantCultureIgnoreCase);

        // Assert
        Assert.Equal("{\"v\": \"A C Acme D B\"}", result);
    }
}

using IniaAi.Core.Services;

namespace IniaAi.Core.Tests.Services;

public class StringReplacerStrategyTests
{
    private readonly StringReplacerStrategy _strategy;

    public StringReplacerStrategyTests()
    {
        _strategy = new StringReplacerStrategy();
    }

    [Fact]
    public void ReplacePlaceholders_WithNullOrEmptyInput_ReturnsInput()
    {
        string? nullInput = null;
        string emptyInput = string.Empty;
        string sourceText = "<test>Some value</test>";

        // Null
        Assert.Null(_strategy.ReplacePlaceholders(nullInput, sourceText));

        // Empty
        Assert.Equal(string.Empty, _strategy.ReplacePlaceholders(emptyInput, sourceText));
    }

    [Fact]
    public void ReplacePlaceholders_WithMatchingTag_ReturnsTagContent()
    {
        string source = @"<test>Here is the content</test>";
        string input = "test";
        Assert.Equal("Here is the content", _strategy.ReplacePlaceholders(input, source));
    }

    [Fact]
    public void ReplacePlaceholders_WithNonMatchingTag_ReturnsNull()
    {
        string source = @"<test>some content</test>";
        string input = "differentTag";
        Assert.Null(_strategy.ReplacePlaceholders(input, source));
    }

    [Fact]
    public void ReplacePlaceholders_WithMultipleMatchingTags_ReturnsAllOccurrences()
    {
        string source = @"
        <test>First Content</test>
        <test>Second Content</test>
        ";
        string input = "test";
        Assert.Equal("First Content\nSecond Content", _strategy.ReplacePlaceholders(input, source), ignoreLineEndingDifferences: true);
    }

    [Fact]
    public void ReplacePlaceholders_WithDifferentCaseTags_FindsTagCaseInsensitive()
    {
        string source = @"<TEST>MixedCaseContent</TEST>";
        string input = "test";
        Assert.Equal("MixedCaseContent", _strategy.ReplacePlaceholders(input, source));
    }

    [Fact]
    public void ReplacePlaceholders_WithTagInMiddleOfSource_ExtractsCorrectContent()
    {
        string source = @"Stuff
        <myTag>
        Content to extract
        </myTag>
        More Stuff
        ";
        string input = "myTag";
        string expected = "Content to extract";
        Assert.Equal(expected, _strategy.ReplacePlaceholders(input, source));
    }

    [Fact]
    public void ReplacePlaceholders_WithSurroundingText_IgnoresOutsideContent()
    {
        string source = @"Prefix
        <someTag>InnerValue</someTag>
        Suffix
        ";
        string input = "someTag";
        Assert.Equal("InnerValue", _strategy.ReplacePlaceholders(input, source));
    }
}

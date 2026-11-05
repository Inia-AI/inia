using IniaAi.Domain.OutputFormat;

namespace IniaAi.Domain.Tests.OutputFormat;

public class TagMatcherTests
{
    [Theory]
    [InlineData("answer", true)]
    [InlineData("Answer", true)]
    [InlineData("_answer", true)]
    [InlineData("answer_1", true)]
    [InlineData("answer-tag", true)]
    [InlineData("a", true)]
    [InlineData("1answer", false)]
    [InlineData("-answer", false)]
    [InlineData("answer.tag", false)]
    [InlineData("answer tag", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidTagName_AcceptsIdentifiersOnly(string? input, bool expected)
    {
        Assert.Equal(expected, TagMatcher.IsValidTagName(input));
    }

    [Fact]
    public void FindFirstTagContent_ReturnsInner_WhenPresent()
    {
        Assert.Equal("yes", TagMatcher.FindFirstTagContent("<answer>yes</answer>", "answer"));
    }

    [Fact]
    public void FindFirstTagContent_ReturnsEmpty_OnEmptyInner()
    {
        Assert.Equal(string.Empty, TagMatcher.FindFirstTagContent("<answer></answer>", "answer"));
    }

    [Fact]
    public void FindFirstTagContent_ReturnsFirst_WhenMultipleMatches()
    {
        Assert.Equal("a", TagMatcher.FindFirstTagContent("<x>a</x><x>b</x>", "x"));
    }

    [Fact]
    public void FindFirstTagContent_ReturnsNull_WhenMissing()
    {
        Assert.Null(TagMatcher.FindFirstTagContent("<other>x</other>", "answer"));
    }

    [Fact]
    public void FindFirstTagContent_ReturnsNull_WhenTagNameInvalid()
    {
        Assert.Null(TagMatcher.FindFirstTagContent("<1bad>x</1bad>", "1bad"));
    }

    [Fact]
    public void FindFirstTagContent_HandlesMultilineContent()
    {
        string text = "<answer>line1\nline2\nline3</answer>";
        Assert.Equal("line1\nline2\nline3", TagMatcher.FindFirstTagContent(text, "answer"));
    }

    [Theory]
    [InlineData(null, new string[0])]
    [InlineData("", new string[0])]
    [InlineData("   ", new string[0])]
    [InlineData("a", new[] { "a" })]
    [InlineData("a,b,c", new[] { "a", "b", "c" })]
    [InlineData(" a , b , c ", new[] { "a", "b", "c" })]
    [InlineData("a,,b", new[] { "a", "b" })]
    [InlineData("a, ,b", new[] { "a", "b" })]
    public void SplitTagNames_HandlesEdgeCases(string? input, string[] expected)
    {
        IReadOnlyList<string> result = TagMatcher.SplitTagNames(input);
        Assert.Equal(expected, result);
    }
}

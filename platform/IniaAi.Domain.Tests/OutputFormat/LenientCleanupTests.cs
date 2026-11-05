using IniaAi.Domain.OutputFormat;

namespace IniaAi.Domain.Tests.OutputFormat;

public class LenientCleanupTests
{
    [Fact]
    public void Clean_PlainText_ReturnsUnchanged()
    {
        string result = LenientCleanup.Clean("hello world");
        Assert.Equal("hello world", result);
    }

    [Fact]
    public void Clean_LeadingTrailingWhitespace_IsTrimmed()
    {
        string result = LenientCleanup.Clean("   hello\n\n");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Clean_FencedWithLanguage_StripsFence()
    {
        string input = "```json\n{\"a\":1}\n```";
        string result = LenientCleanup.Clean(input);
        Assert.Equal("{\"a\":1}", result);
    }

    [Fact]
    public void Clean_FencedWithoutLanguage_StripsFence()
    {
        string input = "```\n{\"a\":1}\n```";
        string result = LenientCleanup.Clean(input);
        Assert.Equal("{\"a\":1}", result);
    }

    [Fact]
    public void Clean_FencedWithSurroundingWhitespace_StripsFenceAndTrims()
    {
        string input = "  \n```json\n{\"a\":1}\n```\n  ";
        string result = LenientCleanup.Clean(input);
        Assert.Equal("{\"a\":1}", result);
    }

    [Fact]
    public void Clean_NullOrEmpty_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, LenientCleanup.Clean(string.Empty));
    }

    [Fact]
    public void Clean_NoFenceCloseTag_LeavesAsIs()
    {
        string input = "```json\n{\"a\":1}";
        string result = LenientCleanup.Clean(input);
        Assert.Equal(input, result);
    }
}

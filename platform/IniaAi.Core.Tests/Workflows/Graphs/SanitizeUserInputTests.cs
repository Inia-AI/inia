using IniaAi.Core.Workflows.Graph;

namespace IniaAi.Core.Tests.Workflows.Graphs;

public class SanitizeUserInputTests
{
    [Fact]
    public void HtmlTags_AreEscaped()
    {
        string input = "<script>alert('xss')</script>";

        string result = GraphQueryService.SanitizeUserInput(input);

        Assert.Equal("&lt;script&gt;alert('xss')&lt;/script&gt;", result);
    }

    [Fact]
    public void DoubleCurlyBraces_AreBroken()
    {
        string input = "Hello {{world}}";

        string result = GraphQueryService.SanitizeUserInput(input);

        Assert.Equal("Hello { {world} }", result);
    }

    [Fact]
    public void TripleBackticks_AreBroken()
    {
        string input = "Use ```code``` here";

        string result = GraphQueryService.SanitizeUserInput(input);

        Assert.Equal("Use ` ` `code` ` ` here", result);
    }

    [Fact]
    public void NullInput_ReturnsNull()
    {
        string result = GraphQueryService.SanitizeUserInput(null!);

        Assert.Null(result);
    }

    [Fact]
    public void EmptyInput_ReturnsEmpty()
    {
        string result = GraphQueryService.SanitizeUserInput(string.Empty);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void WhitespaceInput_ReturnsUnchanged()
    {
        string result = GraphQueryService.SanitizeUserInput("   ");

        Assert.Equal("   ", result);
    }

    [Fact]
    public void NormalText_IsUnchanged()
    {
        string input = "Where does the revenue figure come from?";

        string result = GraphQueryService.SanitizeUserInput(input);

        Assert.Equal(input, result);
    }

    [Fact]
    public void MixedAttackVectors_AllEscaped()
    {
        string input = "<b>{{inject}}</b> and ```exec```";

        string result = GraphQueryService.SanitizeUserInput(input);

        Assert.Equal("&lt;b&gt;{ {inject} }&lt;/b&gt; and ` ` `exec` ` `", result);
    }
}

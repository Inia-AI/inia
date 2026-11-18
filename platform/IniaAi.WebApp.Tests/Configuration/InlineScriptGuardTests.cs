using IniaAi.Testing;
using System.Text.RegularExpressions;

namespace IniaAi.WebApp.Tests.Configuration;

/// <summary>
/// The admin app's content policy runs only the script files the app serves, so the browser blocks a script element
/// with no source, an inline event handler and a <c>javascript:</c> address. None of them may enter the markup.
/// </summary>
public partial class InlineScriptGuardTests
{
    private const string WEB_APP_DIRECTORY = "IniaAi.WebApp";
    private const string MARKUP_FILES = "*.razor";

    [GeneratedRegex("""(?i:<script(?![^>]*\bsrc\s*=)[^>]*>|\bjavascript:)|(?<![@\w:.-])on[a-z]+\s*=\s*["']""")]
    private static partial Regex InlineScript();

    [Fact]
    public void EveryScript_IsAFileTheAppServes()
    {
        string webApp = Path.Combine(RepositoryRoot.Find(), WEB_APP_DIRECTORY);

        List<string> inline = [.. Directory
            .EnumerateFiles(webApp, MARKUP_FILES, SearchOption.AllDirectories)
            .SelectMany(path => File.ReadLines(path)
                .Select((line, index) => (Line: line, Number: index + 1))
                .Where(entry => InlineScript().IsMatch(entry.Line))
                .Select(entry => $"{Path.GetRelativePath(webApp, path).Replace('\\', '/')}:{entry.Number}: {entry.Line.Trim()}"))];

        Assert.True(inline.Count == 0, $"Inline script in the admin app's markup:\n{string.Join('\n', inline)}");
    }

    [Theory]
    [InlineData("<script>window.x = 1;</script>", true)]
    [InlineData("<script type=\"text/javascript\">", true)]
    [InlineData("<button onclick=\"save()\">", true)]
    [InlineData("<a href=\"javascript:void(0)\">", true)]
    [InlineData("<script src=\"js/editor-scroll.js\"></script>", false)]
    [InlineData("<MudButton OnClick=\"Save\">", false)]
    [InlineData("<button @onclick=\"Save\" @onclick:preventDefault=\"true\">", false)]
    public void TheGuard_FindsEveryKindOfInlineScript(string line, bool inline)
    {
        Assert.Equal(inline, InlineScript().IsMatch(line));
    }
}

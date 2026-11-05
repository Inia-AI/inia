using IniaAi.Testing;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// MathJax runs in the browser, so these tests pin the setup it reads: a removed or reordered line here
/// lets model output in the Markdown view make links again.
/// </summary>
public class MathJaxSetupTests
{
    private const string APP_PAGE = "IniaAi.WebApp/Components/App.razor";
    private const string MATHJAX_CONFIG = "IniaAi.WebApp/wwwroot/js/mathjax-config.js";
    private const string CONFIG_SCRIPT = "js/mathjax-config.js";
    private const string MARKDOWN_SCRIPT = "_content/MudBlazor.Markdown/MudBlazor.Markdown.min.js";

    [Fact]
    public void TheAppPage_SetsUpMathJaxBeforeTheMarkdownScriptCanLoadIt()
    {
        string page = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), APP_PAGE));

        int config = page.IndexOf(CONFIG_SCRIPT, StringComparison.Ordinal);
        int markdown = page.IndexOf(MARKDOWN_SCRIPT, StringComparison.Ordinal);

        Assert.True(config >= 0, $"{APP_PAGE} must load {CONFIG_SCRIPT}");
        Assert.True(markdown > config, $"{APP_PAGE} must load {MARKDOWN_SCRIPT} after {CONFIG_SCRIPT}");
    }

    [Theory]
    [InlineData("input: ['tex']")]
    [InlineData("autoload: { html: [] }")]
    [InlineData("allow: { html: false }")]
    public void TheMathJaxConfig_KeepsTheLinkAndStyleCommandsOff(string setting)
    {
        string config = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), MATHJAX_CONFIG));

        Assert.Contains(setting, config, StringComparison.Ordinal);
    }
}

using AngleSharp.Dom;
using Bunit;
using IniaAi.WebApp.Components.WorkflowEditor.Pages;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class NoVariablesNoticeTests : DesignerPageOpeningTest
{
    private const string WORKFLOW_TYPE_ID = "Corp";
    private const string VARIABLES_TAB_CONTENT = "Add Custom Variable";

    public NoVariablesNoticeTests(ITestOutputHelper output)
        : base(output)
    {
    }

    [Fact]
    public async Task TheNoVariablesNotice_NamesTheVariablesTab_AndItsButtonOpensIt()
    {
        IRenderedComponent<WorkflowDesignerPage> page = await OpenAsync<WorkflowDesignerPage>(WORKFLOW_TYPE_ID, parameters => parameters
            .Add(p => p.WorkflowTypeId, WORKFLOW_TYPE_ID));
        page.WaitForAssertion(() => Assert.NotEmpty(page.FindAll(SAVE_BUTTON)));
        Assert.DoesNotContain(VARIABLES_TAB_CONTENT, page.Markup, StringComparison.Ordinal);

        IElement notice = page.FindAll(".mud-alert").Single(alert => alert.TextContent.Contains("no variables", StringComparison.Ordinal));
        Assert.Contains("Variables tab", notice.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("in the workflow editor", notice.TextContent, StringComparison.Ordinal);

        notice.QuerySelector("button")!.Click();

        page.WaitForAssertion(() => Assert.Contains(VARIABLES_TAB_CONTENT, page.Markup, StringComparison.Ordinal));
    }
}

using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Dependencies;
using IniaAi.WebApp.Components.WorkflowEditor.State;
using IniaAi.WebApp.Models;
using MudBlazor.Services;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;
using MudPopoverProvider = MudBlazor.MudPopoverProvider;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Dependencies;

public class DependencyIssuesMenuTests : BunitContext
{
    private const string ISSUE_TEXT = "No block or input supplies {nameParent}";

    [Fact]
    public void AClickOnTheIssueButton_OpensTheListOfIssues_AndASecondClickClosesIt()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
        IRenderedComponent<MudPopoverProvider> popovers = Render<MudPopoverProvider>();
        var workflow = new Workflow("root");
        var section = new Section("Summary", workflow) { Title = "Summary" };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "qa_summary",
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarise {nameParent}." },
        };

        IRenderedComponent<DependencyGraphView> graph = Render<DependencyGraphView>(parameters => parameters
            .Add(p => p.State, new DependencyViewState())
            .AddCascadingValue("EditorContext", new WorkflowEditorContext("Corp", workflow, null!))
            .AddCascadingValue(new EditorSurfaceState(EditorSurfaceCapabilities.FullEditor)));

        graph.WaitForAssertion(() => Assert.NotEmpty(Button(graph, "1 issue")));
        Assert.DoesNotContain(ISSUE_TEXT, popovers.Markup, StringComparison.Ordinal);

        Button(graph, "1 issue").Single().Click();

        popovers.WaitForAssertion(() => Assert.Contains(ISSUE_TEXT, popovers.Find(".mud-popover-open").TextContent, StringComparison.Ordinal));

        Button(graph, "1 issue").Single().Click();

        popovers.WaitForAssertion(() => Assert.Empty(popovers.FindAll(".mud-popover-open")));
    }

    private static IEnumerable<AngleSharp.Dom.IElement> Button(IRenderedComponent<DependencyGraphView> graph, string text)
    {
        return graph.FindAll("button").Where(button => button.TextContent.Trim() == text);
    }
}

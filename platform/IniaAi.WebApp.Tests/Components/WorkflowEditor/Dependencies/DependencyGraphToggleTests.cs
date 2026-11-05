using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApp.Components.WorkflowEditor.Dependencies;
using IniaAi.WebApp.Components.WorkflowEditor.State;
using IniaAi.WebApp.Models;
using MudBlazor.Services;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor.Dependencies;

/// <summary>
/// The graph's view switch and filters show their state by fill alone, so each says it to a screen
/// reader too.
/// </summary>
public class DependencyGraphToggleTests : BunitContext
{
    [Fact]
    public void TheViewSwitchAndTheFilters_SayWhichIsOn()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
        var workflow = new Workflow("root");
        var section = new Section("Summary", workflow) { Title = "Summary" };
        _ = new Block(BlockTypes.AiQuery, section)
        {
            ReplacementTag = "qa_summary",
            Properties = new AiQueryProperties { UserPromptTemplate = "Summarise the figures." },
        };

        IRenderedComponent<DependencyGraphView> graph = Render<DependencyGraphView>(parameters => parameters
            .Add(p => p.State, new DependencyViewState())
            .AddCascadingValue("EditorContext", new WorkflowEditorContext("Corp", workflow, null!))
            .AddCascadingValue(new EditorSurfaceState(EditorSurfaceCapabilities.FullEditor)));

        graph.WaitForAssertion(() => Assert.Equal("true", Button(graph, "Map").GetAttribute("aria-pressed")));
        Assert.Equal("false", Button(graph, "Chain").GetAttribute("aria-pressed"));
        Assert.Equal("Graph view", Button(graph, "Map").Closest("[role=group]")!.GetAttribute("aria-label"));
        Assert.Equal("false", Chip(graph, "Inputs").GetAttribute("aria-pressed"));
        Assert.Equal("false", Chip(graph, "Gates").GetAttribute("aria-pressed"));

        Chip(graph, "Inputs").Click();

        graph.WaitForAssertion(() => Assert.Equal("true", Chip(graph, "Inputs").GetAttribute("aria-pressed")));
    }

    private static IElement Button(IRenderedComponent<DependencyGraphView> graph, string text)
    {
        return graph.FindAll("button").Single(button => button.TextContent.Trim() == text);
    }

    private static IElement Chip(IRenderedComponent<DependencyGraphView> graph, string text)
    {
        return graph.FindAll(".mud-chip").Single(chip => chip.TextContent.Trim() == text);
    }
}

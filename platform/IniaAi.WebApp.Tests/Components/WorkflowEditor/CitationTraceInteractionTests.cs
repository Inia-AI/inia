using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.Entities.Workflows.Responses;
using IniaAi.WebApp.Components.WorkflowEditor.Components.Blocks.Responses;
using MudDialogProvider = MudBlazor.MudDialogProvider;
using MudBlazor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class CitationTraceInteractionTests : BunitContext
{
    private const string PANEL_TITLE = "Citation trace";
    private const string NO_TRACE_MESSAGE = "No citation trace. Either this run was made before the platform recorded citation traces, or this block's output reaches no report paragraph, so it is not a citation source.";

    public CitationTraceInteractionTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
    }

    [Fact]
    public void ATracedBlock_OpensToItsCountsAndFacts()
    {
        (_, _, _, Block draft) = CitationTraceTestRun.Build();

        IRenderedComponent<CitationTraceInteraction> panel = Render<CitationTraceInteraction>(p => p.Add(c => c.Block, draft));
        Open(panel);

        Assert.Contains("1 cited to the block that worked it out", panel.Markup);
        Assert.Contains("Worked out by USD conversion", panel.Markup);
        Assert.Contains(CitationTraceTestRun.CONVERSION_SENTENCE, panel.Markup);
    }

    [Fact]
    public void FollowAFigure_ListsEveryBlockOfTheRunThatStatesIt()
    {
        (_, _, _, Block draft) = CitationTraceTestRun.Build();
        IRenderedComponent<CitationTraceInteraction> panel = Render<CitationTraceInteraction>(p => p.Add(c => c.Block, draft));
        Open(panel);

        panel.Find("input").Input("$55.0m");

        panel.WaitForAssertion(() =>
        {
            Assert.Contains("Turned down: no source of its sentence prints it.", panel.Markup);
            Assert.Contains("Worked out by USD conversion (4 words in common).", panel.Markup);
        });
    }

    [Fact]
    public void APageSource_OpensThePageAtTheCitedSpan()
    {
        (_, Block statement, _, _) = CitationTraceTestRun.Build();
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<CitationTraceInteraction> panel = Render<CitationTraceInteraction>(p => p.Add(c => c.Block, statement));
        Open(panel);

        panel.FindAll("a, .mud-link").First(link => link.TextContent.Contains("page 1 of annual-report.pdf")).Click();

        dialogs.WaitForAssertion(() =>
            Assert.Equal(CitationTraceTestRun.REVENUE_SENTENCE, dialogs.Find("mark").TextContent));
    }

    [Fact]
    public void AnExecutedBlockOfAnUntracedRun_SaysTheRunPredatesTraces()
    {
        Block block = ExecutedBlockOfUntracedRun();

        IRenderedComponent<CitationTraceInteraction> panel = Render<CitationTraceInteraction>(p => p.Add(c => c.Block, block));
        Open(panel);

        Assert.Contains(NO_TRACE_MESSAGE, panel.Markup);
    }

    [Fact]
    public void AnUntracedBlockOfATracedRun_ShowsNoPanel()
    {
        (Workflow workflow, _, _, _) = CitationTraceTestRun.Build();
        Block untraced = new(BlockTypes.AiQuery, workflow.Sections.Single(), "switch") { Name = "Routing" };
        _ = new BlockResponse(untraced) { RawResponse = "AF11" };

        IRenderedComponent<CitationTraceInteraction> panel = Render<CitationTraceInteraction>(p => p.Add(c => c.Block, untraced));

        Assert.DoesNotContain(PANEL_TITLE, panel.Markup);
    }

    [Fact]
    public void ATemplateBlockThatNeverRan_ShowsNoPanel()
    {
        var workflow = new Workflow("Template");
        Block block = new(BlockTypes.AiQuery, new Section("Body", workflow), "draft") { Name = "Draft" };

        IRenderedComponent<CitationTraceInteraction> panel = Render<CitationTraceInteraction>(p => p.Add(c => c.Block, block));

        Assert.DoesNotContain(PANEL_TITLE, panel.Markup);
    }

    private static Block ExecutedBlockOfUntracedRun()
    {
        var workflow = new Workflow("Old run");
        Block block = new(BlockTypes.AiQuery, new Section("Body", workflow), "draft") { Name = "Draft" };
        _ = new BlockResponse(block) { RawResponse = "Revenue was $55.0m." };
        return block;
    }

    private static void Open(IRenderedComponent<CitationTraceInteraction> panel)
    {
        IElement header = panel.Find(".mud-expand-panel-header");
        Assert.Contains(PANEL_TITLE, header.TextContent);
        header.Click();
    }
}

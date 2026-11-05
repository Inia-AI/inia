using Bunit;
using IniaAi.Application.Common.Workflows;
using IniaAi.Core.Workflows.Diff;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;
using MudDialogProvider = MudBlazor.MudDialogProvider;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class WorkflowVersionChangesPanelTests : BunitContext
{
    public WorkflowVersionChangesPanelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
    }

    [Fact]
    public void ASectionWhoseOnlyChangeIsASetting_ShowsThatSettingToOpen()
    {
        var diff = new WorkflowDiff(
            [new SectionDiff("Currency", 0, DiffChangeKind.Modified)
            {
                FieldDiffs = [new FieldDiff("TextInputTemplate", "Library text", "Copy text")],
            }],
            [],
            [],
            []);

        IRenderedComponent<WorkflowVersionChangesPanel> panel =
            Render<WorkflowVersionChangesPanel>(p => p.Add(c => c.Diff, diff));

        Assert.Contains("Currency", panel.Markup);
        Assert.Single(panel.FindAll("button"), button => button.TextContent.Contains("Section text input"));
    }

    [Fact]
    public void BlockOrder_OpensAsWholeLines_SoAMoveDoesNotReadAsARename()
    {
        var diff = new WorkflowDiff(
            [new SectionDiff("Lookback Period", 0, DiffChangeKind.Modified)
            {
                FieldDiffs =
                [
                    new FieldDiff(
                        SharedComponentDefinitionDiffer.BLOCK_ORDER_FIELD,
                        "iSearch Lookback Period\niSearch Lookback Period - Clean",
                        "iSearch Lookback Period - Clean\niSearch Lookback Period"),
                ],
            }],
            [],
            [],
            []);
        IRenderedComponent<MudDialogProvider> dialogs = Render<MudDialogProvider>();
        IRenderedComponent<WorkflowVersionChangesPanel> panel =
            Render<WorkflowVersionChangesPanel>(p => p.Add(c => c.Diff, diff));

        panel.Find("button").Click();

        dialogs.WaitForAssertion(() => Assert.Equal(
            ["iSearch Lookback Period\n", "iSearch Lookback Period"],
            dialogs.FindAll("div.field-diff-pane mark").Select(mark => mark.TextContent)));
    }
}

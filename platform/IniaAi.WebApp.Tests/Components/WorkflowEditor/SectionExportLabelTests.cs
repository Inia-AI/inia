using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using MudBlazor.Services;
using MudPopoverProvider = MudBlazor.MudPopoverProvider;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public class SectionExportLabelTests : BunitContext
{
    private readonly IRenderedComponent<MudPopoverProvider> _popovers;

    public SectionExportLabelTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
        _popovers = Render<MudPopoverProvider>();
    }

    [Fact]
    public void ASectionLeftOutOfExports_IsLabelledForEveryExport()
    {
        IRenderedComponent<SectionActionsMenu> header = RenderSectionHeader(excluded: true);

        Assert.Contains("Not in exports", header.Find(".mud-chip").TextContent, StringComparison.Ordinal);
        Assert.Equal("Include in exports", MenuItemText(header));
    }

    [Fact]
    public void ASectionInTheExports_OffersToLeaveItOutOfEveryExport()
    {
        IRenderedComponent<SectionActionsMenu> header = RenderSectionHeader(excluded: false);

        Assert.Equal("Leave out of exports", MenuItemText(header));
    }

    private string MenuItemText(IRenderedComponent<SectionActionsMenu> header)
    {
        header.Find("button[aria-label^='More section actions']").Click();
        IElement item = _popovers.FindAll(".mud-menu-item").Single(candidate => candidate.TextContent.Contains("exports", StringComparison.Ordinal));
        return item.TextContent.Trim();
    }

    private IRenderedComponent<SectionActionsMenu> RenderSectionHeader(bool excluded)
    {
        Section section = new("Debug trace", new Workflow("test"));
        section.Properties.ExcludeFromWordExport = excluded;

        return Render<SectionActionsMenu>(parameters => parameters
            .Add(p => p.Section, section)
            .Add(p => p.OnRun, () => { })
            .Add(p => p.OnDuplicate, () => { })
            .Add(p => p.OnMove, () => { })
            .Add(p => p.OnCopyToWorkflow, () => { })
            .Add(p => p.OnCopyAsText, () => { })
            .Add(p => p.OnToggleCitations, () => { })
            .Add(p => p.OnRemove, () => { }));
    }
}

using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using MudBlazor.Services;
using MudPopoverProvider = MudBlazor.MudPopoverProvider;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

/// <summary>
/// A screen reader lists a page's buttons by name, so a section's action buttons each name the
/// section they act on.
/// </summary>
public class SectionActionNameTests : BunitContext
{
    private readonly Workflow _workflow = new("test");

    public SectionActionNameTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
        _ = Render<MudPopoverProvider>();
    }

    [Fact]
    public void EveryActionButton_NamesItsSection_SoTwoSectionsShareNoName()
    {
        List<string> first = ButtonNames(RenderSectionHeader("Sec A"));
        List<string> second = ButtonNames(RenderSectionHeader("Sec B"));

        Assert.Equal(9, first.Count);
        Assert.All(first, name => Assert.EndsWith("Sec A", name, StringComparison.Ordinal));
        Assert.All(second, name => Assert.EndsWith("Sec B", name, StringComparison.Ordinal));
        Assert.Empty(first.Intersect(second));
        Assert.Contains("Run section: Sec A", first);
        Assert.Contains("Remove: Sec A", first);
        Assert.Contains("More section actions: Sec A", first);
    }

    [Fact]
    public void AnUntitledSection_IsNamedByItsPlace()
    {
        List<string> names = ButtonNames(RenderSectionHeader(string.Empty));

        Assert.Contains("Run section: section 0", names);
    }

    private static List<string> ButtonNames(IRenderedComponent<SectionActionsMenu> header)
    {
        return [.. header.FindAll("button").Select(button => button.GetAttribute("aria-label") ?? button.TextContent.Trim())];
    }

    private IRenderedComponent<SectionActionsMenu> RenderSectionHeader(string title)
    {
        Section section = new(title, _workflow) { Title = title };

        return Render<SectionActionsMenu>(parameters => parameters
            .Add(p => p.Section, section)
            .Add(p => p.OnShowBlocks, () => { })
            .Add(p => p.OnParameterizedConfig, () => { })
            .Add(p => p.OnRun, () => { })
            .Add(p => p.OnDuplicate, () => { })
            .Add(p => p.OnMove, () => { })
            .Add(p => p.OnCopyToWorkflow, () => { })
            .Add(p => p.OnCopyAsText, () => { })
            .Add(p => p.OnToggleCitations, () => { })
            .Add(p => p.OnRemove, () => { }));
    }
}

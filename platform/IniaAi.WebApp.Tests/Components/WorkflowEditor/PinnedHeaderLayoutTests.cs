using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Bunit;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Testing;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using IniaAi.WebApp.Components.WorkflowEditor.Components;
using IniaAi.WebApp.Components.WorkflowEditor.State;
using MudBlazor.Services;
using MudPopoverProvider = MudBlazor.MudPopoverProvider;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Tests.Components.WorkflowEditor;

public partial class PinnedHeaderLayoutTests : BunitContext
{
    private const string REPLACEMENT_TAG = "draft";
    private const string APP_CSS = "IniaAi.WebApp/wwwroot/app.css";
    private const string OBSERVE_BLOCKS = "iniaStickyHeaders.observeBlocks";

    public PinnedHeaderLayoutTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        _ = Services.AddMudServices();
        _ = Render<MudPopoverProvider>();
    }

    [GeneratedRegex(@"\.((?:block-header|section-header|editor-action-bar)__[a-z-]+)")]
    private static partial Regex PinnedLayoutPart();

    [Fact]
    public void EveryHeaderPartThePinnedStylesArrange_IsRendered()
    {
        string css = File.ReadAllText(Path.Combine(RepositoryRoot.Find(), APP_CSS));
        string[] parts = [.. PinnedLayoutPart().Matches(css).Select(match => match.Groups[1].Value).Distinct()];
        string rendered = RenderBlockHeader().Markup + RenderSectionHeader().Markup;

        Assert.NotEmpty(parts);
        Assert.All(parts, part => Assert.Contains(part, rendered, StringComparison.Ordinal));
    }

    [Fact]
    public void TheBlockHeader_PutsEachControlInThePartThePinnedBarMoves()
    {
        IRenderedComponent<BlockActionsMenu> header = RenderBlockHeader();
        IElement root = header.Find(".block-header");

        Assert.NotNull(root.QuerySelector(".block-header__title .editor-block-name"));
        Assert.NotNull(root.QuerySelector(".editor-action-bar__buttons [aria-label^='Run block']"));
        Assert.NotNull(root.QuerySelector(".editor-action-bar__end [aria-label^='Remove']"));
        Assert.NotNull(root.QuerySelector(".block-header__meta > .block-type-chip"));
        Assert.Contains("Last changed", root.QuerySelector(".block-header__meta > .block-header__stamp")!.TextContent, StringComparison.Ordinal);
        Assert.NotNull(root.QuerySelector(".block-header__meta > .block-header__status .mud-chip"));
    }

    [Fact]
    public void TheSectionHeader_PutsEachControlInThePartThePinnedBarMoves()
    {
        IRenderedComponent<SectionActionsMenu> header = RenderSectionHeader();
        IElement root = header.Find(".section-header");

        Assert.NotNull(root.QuerySelector(".section-header__title [aria-label='Section title']"));
        Assert.NotNull(root.QuerySelector(".editor-action-bar__buttons [aria-label^='Run section']"));
        Assert.NotNull(root.QuerySelector(".editor-action-bar__end [aria-label^='Remove']"));
    }

    [Fact]
    public void TheBlockList_PinsItsBlockHeadersOnEveryScreenThatShowsIt()
    {
        IRenderedComponent<SectionInteraction> list = Render<SectionInteraction>(parameters => parameters
            .Add(p => p.Section, new Section("Component", new Workflow("test")))
            .AddCascadingValue(new EditorSurfaceState(EditorSurfaceCapabilities.Library)));

        IElement scope = list.Find(".block-pin-scope");

        JSInterop.VerifyInvoke(OBSERVE_BLOCKS).Arguments.Single().ShouldBeElementReferenceTo(scope);
    }

    private IRenderedComponent<BlockActionsMenu> RenderBlockHeader()
    {
        Block block = new(BlockTypes.AiQuery, new Section("Body", new Workflow("test")), REPLACEMENT_TAG) { Name = "Draft" };
        Dictionary<string, BlockLastTouchedDto> lastTouched = new()
        {
            [REPLACEMENT_TAG] = new(REPLACEMENT_TAG, "version-1", "3734af0d", new DateTime(2026, 10, 16, 9, 0, 0, DateTimeKind.Utc), "user-1", "Superadmin Account", null),
        };

        return Render<BlockActionsMenu>(parameters => parameters
            .Add(p => p.Block, block)
            .Add(p => p.OnDuplicate, () => { })
            .Add(p => p.OnRemove, () => { })
            .AddCascadingValue<IReadOnlyDictionary<string, BlockLastTouchedDto>>(lastTouched));
    }

    private IRenderedComponent<SectionActionsMenu> RenderSectionHeader()
    {
        Section section = new("Ownership & Group Structure", new Workflow("test"));

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

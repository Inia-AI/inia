using IniaAi.WebApp.Components.WorkflowEditor.State;
using MudBlazor;
using Block = IniaAi.Domain.Entities.Workflows.Blocks.Block;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// How a <see cref="BlockBadge"/> is rendered. The same badge definition drives both: a labelled
/// chip in the block header and a compact icon in the workflow map row.
/// </summary>
public enum BlockBadgeDisplay
{
    Icon,
    Chip,
}

/// <summary>
/// A single at-a-glance indicator shown on a block. Definitions live in <see cref="BlockBadges"/>,
/// add one entry there to introduce a new flag and every render site picks it up automatically.
/// </summary>
/// <param name="Key">Stable identifier, used as the Blazor render key and in tests.</param>
/// <param name="Icon">MudBlazor icon path.</param>
/// <param name="Color">MudBlazor colour for the icon/chip.</param>
/// <param name="Label">Short text shown on the chip variant.</param>
/// <param name="Tooltip">Hover description shown in both variants (also the accessible label).</param>
/// <param name="Accent">Extra CSS class, for badges that carry a hue MudBlazor's palette has no name for.</param>
public record BlockBadge(
    string Key,
    string Icon,
    Color Color,
    string Label,
    string Tooltip,
    string? Accent = null);

/// <summary>
/// The catalog of block badges and the single predicate per badge that decides when it applies.
/// This is the one place to extend: add a <c>(when, badge)</c> entry and both the block header
/// chips and the workflow-map icons render it without touching either component.
/// </summary>
public static class BlockBadges
{
    private static readonly (Func<Block, bool> When, BlockBadge Badge)[] _definitions =
    [
        (static block => ReferencesSignatureFields(block),
            new BlockBadge(
                "signature",
                Icons.Material.Outlined.Draw,
                Color.Secondary,
                "Signatures",
                "This block's templates reference client-editable signature fields")),
        (static block => block.IsOutputSource,
            new BlockBadge(
                "output",
                Icons.Material.Outlined.Output,
                Color.Primary,
                "Output",
                "Shows in Output")),
    ];

    private static bool ReferencesSignatureFields(Block block)
    {
        Domain.Entities.Workflows.Workflow? workflow = block.Section?.Workflow;
        if (workflow is null
            || workflow.SignatureFields.Count == 0
            || block.Properties is not Domain.Entities.Workflows.Blocks.Properties.Abstractions.IHasSignatureFields)
        {
            return false;
        }

        return workflow.SignatureFields.Any(f =>
            !string.IsNullOrWhiteSpace(f.Name)
            && block.Properties.HasPlaceholder(
                Domain.Entities.Workflows.Workflow.SignatureFieldToken(f.Name),
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The badges that apply to <paramref name="block"/>, in catalog order.
    /// </summary>
    public static IEnumerable<BlockBadge> For(Block block)
    {
        return For(block, linkIndex: null);
    }

    /// <summary>
    /// Catalog badges plus the block's Library link badge, resolved through
    /// <paramref name="linkIndex"/> when one is available. A null index (registry snapshot not
    /// loaded) simply omits the Library badge.
    /// </summary>
    public static IEnumerable<BlockBadge> For(Block block, LibraryLinkIndex? linkIndex)
    {
        foreach ((Func<Block, bool> when, BlockBadge badge) in _definitions)
        {
            if (when(block))
            {
                yield return badge;
            }
        }

        if (linkIndex is null)
        {
            yield break;
        }

        BlockBadge? libraryBadge = LibraryBadgeFor(linkIndex.For(block));
        if (libraryBadge is not null)
        {
            yield return libraryBadge;
        }
    }

    /// <summary>
    /// The badge for a node's Library link status — shared by the block strip and the section
    /// title chip so both surfaces speak the same visual language.
    /// </summary>
    public static BlockBadge? LibraryBadgeFor(LibraryLinkStatus status)
    {
        BlockBadge? badge = status.Indicator switch
        {
            LibraryLinkIndicator.UpToDate => new BlockBadge(
                "library",
                Icons.Material.Outlined.LibraryBooks,
                Color.Default,
                "Library",
                $"Linked to Library component '{status.ComponentName}'"),
            LibraryLinkIndicator.UpdateAvailable => new BlockBadge(
                "library-update",
                Icons.Material.Outlined.Sync,
                Color.Warning,
                "Update available",
                $"A newer version of '{status.ComponentName}' exists in the Library"),
            LibraryLinkIndicator.Missing => new BlockBadge(
                "library-missing",
                Icons.Material.Outlined.LinkOff,
                Color.Error,
                "Library link broken",
                "This copy links to a component that no longer exists in the Library"),
            LibraryLinkIndicator.Retired => new BlockBadge(
                "library-retired",
                Icons.Material.Outlined.Archive,
                Color.Default,
                "Retired in Library",
                $"'{status.ComponentName}' was retired in the Library"),
            _ => null,
        };

        return badge is null
            ? null
            : badge with { Accent = LibraryAccent.BadgeAccent(status.Indicator) };
    }
}

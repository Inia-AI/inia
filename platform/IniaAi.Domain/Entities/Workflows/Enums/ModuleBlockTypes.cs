namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// The block types whose code is a closed module. The open build keeps them in
/// <see cref="BlockTypes"/> and shows their blocks as stubs: a workflow keeps such a block with
/// its stored settings, and a run fails it with <see cref="UnavailableBlockTypes.RunMessage"/>.
/// </summary>
public static class ModuleBlockTypes
{
    private static readonly HashSet<BlockTypes> _all =
    [
        BlockTypes.Json2Xlsx,
        BlockTypes.PeerFinancials,
        BlockTypes.Verification,
    ];

    public static IReadOnlySet<BlockTypes> All => _all;

    public static bool Contains(BlockTypes type)
    {
        return _all.Contains(type);
    }
}

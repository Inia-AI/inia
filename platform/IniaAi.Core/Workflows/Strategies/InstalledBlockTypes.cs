using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Strategies;

/// <summary>
/// The block types this build can run. A closed module's type or an extension's type runs only
/// when its strategy is registered; a type the <see cref="BlockTypeCatalog"/> lacks never runs.
/// The editor keeps an unavailable block as saved, and a run fails it.
/// </summary>
public sealed class InstalledBlockTypes
{
    private readonly HashSet<BlockTypes> _withStrategy;

    public InstalledBlockTypes(IEnumerable<BlockTypes> withStrategy)
    {
        _withStrategy = [.. withStrategy];
    }

    public bool IsUnavailable(BlockTypes type)
    {
        return UnavailableBlockTypes.IsOutsideThePlatform(type) && !_withStrategy.Contains(type);
    }
}

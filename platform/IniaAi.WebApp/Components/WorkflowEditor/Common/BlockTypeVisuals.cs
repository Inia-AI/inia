using IniaAi.Core.Workflows.Strategies;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class BlockTypeVisuals
{
    private const string UNAVAILABLE_SHORT_LABEL = "Unavailable";

    public static readonly IReadOnlyList<BlockTypes> Selectable =
    [
        BlockTypes.AiQuery,
        BlockTypes.Agent,
        BlockTypes.Verification,
        BlockTypes.Merge,
        BlockTypes.StringManipulation,
        BlockTypes.IntelligentSearch,
        BlockTypes.SimpleRag,
        BlockTypes.CalculateNumber,
        BlockTypes.BuildTopicsForAllDocuments,
        BlockTypes.LoopStart,
        BlockTypes.LoopEnd,
        BlockTypes.ExchangeRate,
        BlockTypes.WebApiCall,
        BlockTypes.DynamicChart,
        BlockTypes.Json2Xlsx,
        BlockTypes.SwitchCase,
        BlockTypes.MergeJson,
        BlockTypes.PeerFinancials,
    ];

    private static readonly Dictionary<BlockTypes, int> _selectableOrder =
        Selectable.Select(static (type, index) => (type, index)).ToDictionary(static entry => entry.type, static entry => entry.index);

    /// <summary>
    /// The types a block can take: every type in the <see cref="BlockTypeCatalog"/>, the platform's in
    /// <see cref="Selectable"/> order, then each installed extension's. The agent family follows the
    /// agent runtime's authoring gate: the runtime is disabled by default while the feature is
    /// incubating (mirrors the save-time gate in <c>SaveWorkflowCommandValidator</c>), so the selector
    /// must not offer a type that would be rejected on save. A type this build cannot run is offered
    /// only to a block that already has it, so the selector can show the block's own type.
    /// </summary>
    public static IReadOnlyList<BlockTypes> SelectableFor(
        bool agentRuntimeEnabled,
        InstalledBlockTypes installedTypes,
        BlockTypes currentType)
    {
        List<BlockTypes> offered = [.. BlockTypeCatalog.All
            .Select(static definition => definition.Type)
            .Where(type => agentRuntimeEnabled || !type.IsAgentFamily())
            .Where(type => !installedTypes.IsUnavailable(type) || type == currentType)
            .OrderBy(static type => _selectableOrder.GetValueOrDefault(type, Selectable.Count))];
        if (!offered.Contains(currentType))
        {
            offered.Add(currentType);
        }

        return offered;
    }

    public static string Label(BlockTypes type) => BlockTypeLabels.Of(type);

    public static string ShortLabel(BlockTypes type) =>
        BlockTypeCatalog.Find(type) is { } definition ? definition.ShortLabel ?? definition.Label : UNAVAILABLE_SHORT_LABEL;
}

using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Replay;

/// <summary>
/// Single source of truth for which block types perform a live external fetch and are replayed
/// from a source execution — by the dormant regression rerun and by the freeze submission option
/// alike — so only workflow logic is tested. Verified against the strategy registrations in
/// WorkflowHandler: IntelligentSearch maps to the web-search strategy, ExchangeRate to the live FX
/// strategy, WebApiCall to the external-API strategy. A new live-fetch block type goes here and
/// nowhere else. Agent-family blocks (Agent, Verification) are deliberately absent: an agent has
/// no internet access, one agent can stand in for a whole workflow, and replaying a member or its
/// verifier would skip the redo loop — the very thing a regression over a verified template tests.
/// </summary>
public static class FreezePolicy
{
    public static readonly IReadOnlySet<BlockTypes> FreezeBlockTypes = new HashSet<BlockTypes>
    {
        BlockTypes.IntelligentSearch,
        BlockTypes.ExchangeRate,
        BlockTypes.WebApiCall,
    };

    public static bool IsFreezeEligible(Block block)
    {
        return FreezeBlockTypes.Contains(block.Type);
    }
}

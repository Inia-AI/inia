using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Domain.Replay;

/// <summary>
/// The freeze capture walk over an executed workflow clone: every freeze-eligible block with a
/// stored response, keyed by stable block id. The freeze submission option resolves a source
/// execution through it, and the case-bundle export uses the same walk so the captures a bundle
/// carries are exactly the captures a same-instance freeze would serve.
/// </summary>
public static class FreezeCaptureWalk
{
    public static IReadOnlyList<ReplayOutput> Collect(Workflow executedClone)
    {
        var captures = new List<ReplayOutput>();

        foreach (Section section in executedClone.Sections)
        {
            foreach (Block block in section.Blocks)
            {
                if (block.BlockResponse is null || !FreezePolicy.IsFreezeEligible(block))
                {
                    continue;
                }

                captures.Add(new ReplayOutput(
                    section.Title ?? string.Empty,
                    block.ReplacementTag ?? string.Empty,
                    block.Type.ToString(),
                    block.BlockResponse.RawResponse,
                    block.BlockResponse.JsonResponse,
                    block.StableBlockId));
            }
        }

        return captures;
    }
}

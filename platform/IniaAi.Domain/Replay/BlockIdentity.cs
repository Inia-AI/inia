using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Domain.Replay;

/// <summary>
/// Outcome of resolving a block identity against a workflow index.
/// </summary>
public enum BlockMatchKind
{
    Matched,
    NotFound,
    Ambiguous,
}

/// <summary>
/// Result of resolving a block identity. <see cref="Block"/> is set only when
/// <see cref="Kind"/> is <see cref="BlockMatchKind.Matched"/>.
/// </summary>
public sealed record BlockMatchResult(
    BlockMatchKind Kind,
    Block? Block,
    int CandidateCount,
    IReadOnlyList<Block>? Candidates = null);

/// <summary>
/// Shared 1 / 0 / many resolver used by both regression replay injection and the comparison
/// block matcher. The stable block id is the only cross-instance key: block GUIDs are minted
/// fresh per execution, and stable ids survive cloning, renames, and loop/parameterized
/// expansion (which suffixes them deterministically). Exactly one match resolves, zero is
/// NotFound, more than one is Ambiguous — a block is never guessed silently. A block without
/// a stable id never matches; baselines must come from templates saved since stable ids were
/// introduced (the save path backfills them).
/// </summary>
public static class BlockIdentityMatcher
{
    public static IReadOnlyDictionary<string, List<Block>> BuildStableIndex(Workflow workflow)
    {
        var index = new Dictionary<string, List<Block>>();

        foreach (Section section in workflow.Sections)
        {
            AddSectionToStableIndex(index, section);
        }

        return index;
    }

    /// <summary>
    /// Index scoped to a single section. Used to re-resolve pending replay captures against the
    /// per-iteration clones a runtime loop expansion just created, without touching other
    /// sections that may be executing concurrently.
    /// </summary>
    public static IReadOnlyDictionary<string, List<Block>> BuildStableIndex(Section section)
    {
        var index = new Dictionary<string, List<Block>>();
        AddSectionToStableIndex(index, section);
        return index;
    }

    private static void AddSectionToStableIndex(
        Dictionary<string, List<Block>> index,
        Section section)
    {
        foreach (Block block in section.Blocks)
        {
            if (string.IsNullOrEmpty(block.StableBlockId))
            {
                continue;
            }

            if (!index.TryGetValue(block.StableBlockId, out List<Block>? bucket))
            {
                bucket = [];
                index[block.StableBlockId] = bucket;
            }

            bucket.Add(block);
        }
    }

    public static BlockMatchResult Resolve(
        string? stableBlockId,
        IReadOnlyDictionary<string, List<Block>> stableIndex)
    {
        if (string.IsNullOrEmpty(stableBlockId))
        {
            return new BlockMatchResult(BlockMatchKind.NotFound, null, 0);
        }

        stableIndex.TryGetValue(stableBlockId, out List<Block>? bucket);

        if (bucket is null || bucket.Count == 0)
        {
            return new BlockMatchResult(BlockMatchKind.NotFound, null, 0);
        }

        if (bucket.Count > 1)
        {
            return new BlockMatchResult(BlockMatchKind.Ambiguous, null, bucket.Count, bucket);
        }

        return new BlockMatchResult(BlockMatchKind.Matched, bucket[0], 1);
    }
}

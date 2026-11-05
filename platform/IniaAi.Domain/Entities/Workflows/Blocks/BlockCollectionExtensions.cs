namespace IniaAi.Domain.Entities.Workflows.Blocks;

public static class BlockCollectionExtensions
{
    /// <summary>
    /// Every block carrying the tag — duplicates included, so a caller judges all of them
    /// rather than silently picking the first.
    /// </summary>
    public static List<Block> CarryingTag(this IEnumerable<Block> blocks, string replacementTag)
    {
        return [.. blocks.Where(block =>
            string.Equals(block.ReplacementTag, replacementTag, StringComparison.Ordinal))];
    }
}

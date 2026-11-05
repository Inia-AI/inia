using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class DuplicateTagDisclosure
{
    public static string? Describe(Block block, Workflow? root)
    {
        if (root is null || string.IsNullOrWhiteSpace(block.ReplacementTag))
        {
            return null;
        }

        List<Block> sharing =
        [
            .. root.GetAllBlocks()
                .Where(other => !ReferenceEquals(other, block)
                    && !ReferenceEquals(other.Section, block.Section)
                    && string.Equals(other.ReplacementTag, block.ReplacementTag, ReplacementTags.Comparison))
        ];

        if (sharing.Count == 0)
        {
            return null;
        }

        int otherCount = sharing.Count - 1;
        string others = otherCount > 0
            ? $" and {otherCount} other block{(otherCount == 1 ? string.Empty : "s")}"
            : string.Empty;

        return $"Also used by '{DescribeBlock(sharing[0])}' in {DescribeSection(sharing[0].Section)}{others}"
            + " — a reference to this tag resolves to whichever block runs first.";
    }

    private static string DescribeBlock(Block block)
    {
        return string.IsNullOrWhiteSpace(block.Name) ? block.ReplacementTag : block.Name;
    }

    private static string DescribeSection(Section? section)
    {
        if (section is null)
        {
            return "another section";
        }

        string label = !string.IsNullOrWhiteSpace(section.Title) ? section.Title : section.Name;

        return string.IsNullOrWhiteSpace(label) ? "another section" : $"'{section.Order}. {label}'";
    }
}

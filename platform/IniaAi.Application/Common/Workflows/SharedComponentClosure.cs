using IniaAi.Application.Queries.SharedComponents;
using IniaAi.Domain.Abstractions.Entities;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Application.Common.Workflows;

internal static class SharedComponentClosure
{
    internal static async Task<IReadOnlyList<string>> CollectAsync(
        ICoreUnitOfWork coreUnitOfWork,
        Workflow template,
        CancellationToken cancellationToken)
    {
        var componentIds = new HashSet<string>(StringComparer.Ordinal);
        var linkedSectionIds = new List<string>();

        foreach (Section section in template.Sections)
        {
            if (section.IsActivelyLinked() && componentIds.Add(section.SharedComponentId!))
            {
                linkedSectionIds.Add(section.SharedComponentId!);
            }
        }

        foreach (Block block in template.GetAllBlocks())
        {
            if (block.IsActivelyLinked())
            {
                _ = componentIds.Add(block.SharedComponentId!);
            }
        }

        if (linkedSectionIds.Count > 0)
        {
            Dictionary<string, Section> sectionHeads =
                await SharedComponentProjections.GetHeadSectionDefinitionsAsync(
                    coreUnitOfWork, linkedSectionIds, cancellationToken);
            foreach (Section headDefinition in sectionHeads.Values)
            {
                foreach (string refTargetId in SharedComponentRefHeads.CollectRefTargetIds(headDefinition))
                {
                    _ = componentIds.Add(refTargetId);
                }
            }
        }

        return [.. componentIds.Order(StringComparer.Ordinal)];
    }
}

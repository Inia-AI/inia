using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.PreviewSharedComponentReconcile;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

public static class SharedComponentImpactText
{
    public static IReadOnlyList<SharedComponentOverrideImpact> OrphansOf(
        SharedComponentReconcilePreviewResult? preview)
    {
        return preview?.UpdateSharedImpact.OrphanedSignatureOverrides ?? [];
    }

    public static List<KeyValuePair<string, string>> SlotRenames(IReadOnlyDictionary<string, string>? bindings)
    {
        return [.. (bindings ?? new Dictionary<string, string>())
            .Where(binding => !string.Equals(binding.Key, binding.Value, StringComparison.Ordinal))
            .OrderBy(binding => binding.Key, StringComparer.Ordinal)];
    }

    public static string DescribeLinkedCopies(IReadOnlyCollection<SharedComponentConsumer> consumers)
    {
        int copies = consumers.Sum(c => c.LinkedInstanceCount);
        string copyLabel = copies == 1 ? "1 linked copy" : $"{copies} linked copies";
        string typeLabel = consumers.Count == 1 ? "1 workflow type" : $"{consumers.Count} workflow types";

        return $"{copyLabel} across {typeLabel}";
    }

    public static string Describe(SharedComponentOverrideImpact orphan)
    {
        string subject = orphan.IsPending ? "Pending client override" : "Client override";
        string effect = orphan.Kind == SharedComponentOverrideImpactKind.Deleted
            ? "will be deleted"
            : "will stop being applied";

        return $"{subject} '{orphan.PlaceholderName}' on block '{orphan.BlockTag}'"
            + $" (org {orphan.OrganizationId}, {orphan.WorkflowTypeId}) {effect}.";
    }

    public static string RefEntryPushScope(string? sectionComponentName)
    {
        string scope = string.IsNullOrWhiteSpace(sectionComponentName)
            ? "this section"
            : $"section '{sectionComponentName}'";

        return $"Pushes to the Library block, including every workflow using {scope}.";
    }

    public static string SectionPushPointerNote()
    {
        return "Referenced Library blocks travel as pointers — pushing the section shares the pointer,"
            + " not their content.";
    }

    public static string HiddenEntriesPushWarning(int hiddenCount)
    {
        string subject = hiddenCount == 1 ? "1 entry hidden here" : $"{hiddenCount} entries hidden here";

        return $"{subject} will be removed from the Library definition for everyone.";
    }

    public static string LocalInsertPushNote()
    {
        return "This block becomes part of the Library section for everyone.";
    }

    public static List<string> DescribeSectionReconcile(SectionReconcileResult result)
    {
        List<string> lines = [];
        foreach ((string oldSlot, string newSlot) in result.RenamedSlots.OrderBy(r => r.Key, StringComparer.Ordinal))
        {
            lines.Add($"Slot '{oldSlot}' was renamed to '{newSlot}' — matched content updated in place.");
        }

        foreach (Block preserved in result.PreservedLocalBlocks)
        {
            string label = string.IsNullOrWhiteSpace(preserved.Name) ? preserved.ReplacementTag : preserved.Name;
            lines.Add($"Block '{label}' left the Library definition but stays in this workflow.");
        }

        foreach (string staleSlot in result.StaleRefSlots)
        {
            lines.Add($"Slot '{staleSlot}' kept its previous content — its referenced Library block could not be resolved.");
        }

        foreach (string droppedSlot in result.DroppedRefSlots)
        {
            lines.Add($"Slot '{droppedSlot}' landed empty — its referenced Library block could not be resolved.");
        }

        return lines;
    }
}

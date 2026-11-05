using ErrorOr;

namespace IniaAi.Application.Common.Workflows;

public static class SharedComponentOrphanMetadata
{
    public const string ORPHANED_OVERRIDES_KEY = "orphanedOverrides";
    public const string OVERWRITTEN_ENTRY_SLOTS_KEY = "overwrittenEntrySlots";
    public const string WORKFLOW_TYPE_ID_KEY = "workflowTypeId";

    public static IReadOnlyList<SharedComponentOverrideImpact> ReadOrphanedOverrides(Error error)
    {
        return error.Metadata?.GetValueOrDefault(ORPHANED_OVERRIDES_KEY)
            as IReadOnlyList<SharedComponentOverrideImpact> ?? [];
    }

    public static (string WorkflowTypeId, IReadOnlyList<string> SlotTags) ReadOverwrittenEntries(Error error)
    {
        return (
            error.Metadata?.GetValueOrDefault(WORKFLOW_TYPE_ID_KEY) as string ?? string.Empty,
            error.Metadata?.GetValueOrDefault(OVERWRITTEN_ENTRY_SLOTS_KEY) as IReadOnlyList<string> ?? []);
    }
}

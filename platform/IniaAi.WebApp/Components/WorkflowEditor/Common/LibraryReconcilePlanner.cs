using IniaAi.Application.Commands.SharedComponents.ApplySharedComponentReconcile;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.SharedComponents.GetWorkflowSharedComponentReconcile;
using IniaAi.Domain.Entities.SharedComponents.Enums;

namespace IniaAi.WebApp.Components.WorkflowEditor.Common;

/// <summary>
/// Pure decision policy behind <c>LibraryReconcileDrawer</c>: which action each row and entry
/// preselects, which rows the quiet switch hides, when entry selects are live, and how the
/// chosen actions translate into apply decisions with their concurrency tokens. Preselects are
/// deliberately conservative — a row acts by default only when the action is the obviously
/// right one (adopt a moved head, break out of a vanished component, formalize a local
/// deletion as a hide); everything else rests at KeepLocal even where the reconciler's arms
/// lead with a stronger action.
/// </summary>
public static class LibraryReconcilePlanner
{
    /// <summary>
    /// Why Apply refuses while the editor holds unsaved changes. Opening the drawer writes
    /// nothing, so it compares the last saved version, and an apply saves that version with the
    /// decisions and reloads it — the unsaved changes would be dropped.
    /// </summary>
    public const string UNSAVED_CHANGES_REFUSAL =
        "Save your editor changes first: applying reloads the template from its last saved version, "
        + "which would discard them.";

    public static SharedComponentReconcileAction DefaultActionFor(SharedComponentInstanceReconcileRow row)
    {
        if (row.LinkState == SharedComponentLinkState.Linked
            && row.Relation == SharedComponentRelation.HeadAhead
            && row.AvailableActions.Contains(SharedComponentReconcileAction.AdoptHead))
        {
            return SharedComponentReconcileAction.AdoptHead;
        }

        if (row.LinkState == SharedComponentLinkState.Linked
            && row.Relation == SharedComponentRelation.ComponentMissing
            && row.AvailableActions.Contains(SharedComponentReconcileAction.BreakOut))
        {
            return SharedComponentReconcileAction.BreakOut;
        }

        return SharedComponentReconcileAction.KeepLocal;
    }

    public static SharedComponentReconcileAction DefaultEntryAction(SharedComponentEntryReconcileRow entry)
    {
        if (entry.OverrideState == SharedComponentEntryOverrideState.Hidden || entry.AvailableActions.Count == 0)
        {
            return SharedComponentReconcileAction.KeepLocal;
        }

        SharedComponentReconcileAction leading = entry.AvailableActions[0];
        if (leading == SharedComponentReconcileAction.Hide)
        {
            return SharedComponentReconcileAction.Hide;
        }

        if (leading == SharedComponentReconcileAction.AdoptHead
            && entry.Relation == SharedComponentRelation.HeadAhead)
        {
            return SharedComponentReconcileAction.AdoptHead;
        }

        if (leading == SharedComponentReconcileAction.BreakOut
            && entry.Relation == SharedComponentRelation.ComponentMissing)
        {
            return SharedComponentReconcileAction.BreakOut;
        }

        return SharedComponentReconcileAction.KeepLocal;
    }

    public static string RelationLabel(SharedComponentRelation relation)
    {
        return relation switch
        {
            SharedComponentRelation.InSync => "Up to date",
            SharedComponentRelation.LocalAhead => "Local changes",
            SharedComponentRelation.HeadAhead => "Update available",
            SharedComponentRelation.Diverged => "Diverged",
            SharedComponentRelation.ComponentMissing => "Missing from Library",
            SharedComponentRelation.ComponentRetired => "Retired",
            _ => relation.ToString(),
        };
    }

    public static string? EntryBadgeLabel(SharedComponentEntryReconcileRow entry)
    {
        return entry.Relation is { } relation && relation != SharedComponentRelation.InSync
            ? RelationLabel(relation)
            : null;
    }

    public static string RowBadgeLabel(SharedComponentInstanceReconcileRow row)
    {
        string label = RelationLabel(row.Relation);
        if (row.Relation != SharedComponentRelation.InSync || row.LocalInsertCount <= 0)
        {
            return label;
        }

        string additions = row.LocalInsertCount == 1
            ? "1 local addition"
            : $"{row.LocalInsertCount} local additions";

        return $"{label} · {additions}";
    }

    public static bool HasContentDifference(SharedComponentInstanceReconcileRow row)
    {
        return row.Relation is SharedComponentRelation.LocalAhead
            or SharedComponentRelation.HeadAhead
            or SharedComponentRelation.Diverged;
    }

    /// <summary>
    /// The copies Library sync lists. A broken-out copy no longer follows its component, so the
    /// template shows it as local content: Library sync leaves out a broken-out copy and a block
    /// broken out of a linked section's slot, and lists a broken-out block added inside a linked
    /// section as a plain local addition.
    /// </summary>
    public static IReadOnlyList<SharedComponentInstanceReconcileRow> Listed(
        IReadOnlyList<SharedComponentInstanceReconcileRow> rows)
    {
        return [.. rows
            .Where(row => row.LinkState != SharedComponentLinkState.BrokenOut)
            .Select(row => row.Entries is null ? row : row with
            {
                Entries = [.. row.Entries
                    .Where(entry => entry.OverrideState != SharedComponentEntryOverrideState.BrokenOut)
                    .Select(AsListedEntry)],
            })];
    }

    private static SharedComponentEntryReconcileRow AsListedEntry(SharedComponentEntryReconcileRow entry)
    {
        if (entry.InsertLinkState != SharedComponentLinkState.BrokenOut)
        {
            return entry;
        }

        return entry with
        {
            RefSharedComponentId = null,
            RefComponentName = null,
            RefComponentState = null,
            RefHeadVersionId = null,
            RefHeadContentHash = null,
            Relation = null,
            InsertLinkState = null,
            AvailableActions = SharedComponentReconciler.EntryAvailableActions(
                entry.Origin,
                entry.OverrideState,
                relation: null,
                insertLinkState: null,
                entry.DeletedByDefinition,
                liveMissing: false),
        };
    }

    public static bool IsQuietRow(SharedComponentInstanceReconcileRow row)
    {
        return row.LinkState == SharedComponentLinkState.Linked
            && row.Relation == SharedComponentRelation.InSync;
    }

    public static bool IsQuietEntry(SharedComponentEntryReconcileRow entry)
    {
        return entry.OverrideState == SharedComponentEntryOverrideState.None
            && !entry.DeletedByDefinition
            && entry.Relation == SharedComponentRelation.InSync;
    }

    public static bool RequiresHideConfirmation(SharedComponentEntryReconcileRow? entry)
    {
        return entry is not null
            && entry.Origin != SharedComponentEntryOrigin.LocalInsert
            && entry.OverrideState == SharedComponentEntryOverrideState.None
            && !entry.DeletedByDefinition;
    }

    public static bool ShouldShow(SharedComponentInstanceReconcileRow row)
    {
        if (!IsQuietRow(row))
        {
            return true;
        }

        return row.Entries is not null && row.Entries.Any(entry => !IsQuietEntry(entry));
    }

    public static int QuietCount(IReadOnlyList<SharedComponentInstanceReconcileRow>? rows)
    {
        if (rows is null)
        {
            return 0;
        }

        int count = 0;
        foreach (SharedComponentInstanceReconcileRow row in rows)
        {
            if (!ShouldShow(row))
            {
                count++;
                continue;
            }

            count += row.Entries?.Count(IsQuietEntry) ?? 0;
        }

        return count;
    }

    public static bool EntrySelectsEnabled(SharedComponentReconcileAction instanceAction)
    {
        return instanceAction == SharedComponentReconcileAction.KeepLocal;
    }

    public static string EntryActionKey(
        SharedComponentInstanceReconcileRow row,
        SharedComponentEntryReconcileRow entry)
    {
        return $"{row.TargetPath}::{entry.EntryStableId ?? entry.EntryKey}";
    }

    public static List<SharedComponentReconcileDecision> BuildDecisions(
        IReadOnlyList<SharedComponentInstanceReconcileRow> rows,
        IReadOnlyDictionary<string, SharedComponentReconcileAction> instanceActions,
        IReadOnlyDictionary<string, SharedComponentReconcileAction> entryActions,
        bool acknowledgeOrphans,
        bool acknowledgeOverwrittenEntries = false)
    {
        List<SharedComponentReconcileDecision> decisions = [];
        foreach (SharedComponentInstanceReconcileRow row in rows)
        {
            SharedComponentReconcileAction action = instanceActions.GetValueOrDefault(
                row.TargetPath, SharedComponentReconcileAction.KeepLocal);
            if (action != SharedComponentReconcileAction.KeepLocal)
            {
                bool needsHead = action is SharedComponentReconcileAction.UpdateShared
                    or SharedComponentReconcileAction.AdoptHead;
                decisions.Add(new SharedComponentReconcileDecision(
                    row.SharedComponentId,
                    TargetStableId: row.TargetStableId,
                    TargetPath: string.IsNullOrEmpty(row.TargetStableId) ? row.TargetPath : null,
                    Action: action,
                    ExpectedHeadVersionId: needsHead ? row.HeadVersionId : null,
                    AcknowledgeSignatureOrphans: acknowledgeOrphans,
                    AcknowledgeOverwrittenEntries: acknowledgeOverwrittenEntries));

                continue;
            }

            if (row.Entries is null)
            {
                continue;
            }

            foreach (SharedComponentEntryReconcileRow entry in row.Entries)
            {
                SharedComponentReconcileAction entryAction = entryActions.GetValueOrDefault(
                    EntryActionKey(row, entry), SharedComponentReconcileAction.KeepLocal);
                if (entryAction == SharedComponentReconcileAction.KeepLocal)
                {
                    continue;
                }

                bool needsHead = entryAction is SharedComponentReconcileAction.UpdateShared
                    or SharedComponentReconcileAction.AdoptHead;
                bool byStableId = !string.IsNullOrEmpty(entry.EntryStableId);
                decisions.Add(new SharedComponentReconcileDecision(
                    row.SharedComponentId,
                    TargetStableId: row.TargetStableId,
                    TargetPath: string.IsNullOrEmpty(row.TargetStableId) ? row.TargetPath : null,
                    Action: entryAction,
                    ExpectedHeadVersionId: needsHead ? row.HeadVersionId : null,
                    AcknowledgeSignatureOrphans: acknowledgeOrphans,
                    EntryKey: byStableId ? null : entry.EntryKey,
                    EntryStableId: byStableId ? entry.EntryStableId : null,
                    ExpectedRefHeadVersionId: needsHead ? entry.RefHeadVersionId : null,
                    AcknowledgeOverwrittenEntries: acknowledgeOverwrittenEntries));
            }
        }

        return decisions;
    }
}

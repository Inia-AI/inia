using System.Text.Json;

namespace IniaAi.Core.Workflows.Activity;

/// <summary>
/// The cached activity script of one workflow version: the version's blocks flattened into
/// authored order, partitioned into contiguous groups that each carry one plain-language line
/// (or none, for housekeeping a reader learns nothing from). Generated once per version and
/// stored on the version row, so a running execution resolves its current step from facts it
/// already persists — which blocks have completed — without any per-run model work.
/// </summary>
public sealed record WorkflowActivityScript(
    IReadOnlyList<WorkflowActivityGroup> Groups,
    IReadOnlyList<string?> BlockIds)
{
    private const char STABLE_ID_SUFFIX_SEPARATOR = '_';

    /// <summary>
    /// Resolves the run's current step from the stable ids of the blocks that are no longer
    /// pending — completed, failed, or skipped rows all count, because each means the work is
    /// over. Blocks are addressed by their template <c>StableBlockId</c> because loop and
    /// parameterized expansion clone blocks under deterministic suffixed ids
    /// (<c>{templateId}_{variantKey}</c>) while renumbering every order after them — so a
    /// completed variant counts for its template block, a block with no stable id never gates
    /// progress, and the current group is the earliest one still holding unfinished work
    /// (honest under the engine's parallel execution, where a slow early block can outlive
    /// later ones). Two accuracy tradeoffs are accepted: the earliest-unfinished rule
    /// under-reports the parallel frontier, and the first finished variant of an expanded
    /// block counts for the whole template block, so a long loop's group advances early —
    /// requiring every variant was rejected because iteration counts are unknowable from the
    /// template and a group mixing differently-sized loops could then never complete, pinning
    /// the line on work that ended. An unlabeled group borrows the nearest labeled neighbour
    /// so the line never goes blank mid-run.
    /// </summary>
    public WorkflowActivityProgress ResolveProgress(IReadOnlyCollection<string> completedBlockIds)
    {
        var completedExactly = new HashSet<string>(completedBlockIds, StringComparer.Ordinal);
        List<string> completedVariants = [.. completedBlockIds
            .Where(completedId => completedId.Contains(STABLE_ID_SUFFIX_SEPARATOR, StringComparison.Ordinal))];

        List<WorkflowActivityProgressStep> steps = [];
        foreach (WorkflowActivityGroup group in Groups)
        {
            if (group.Label is not string label)
            {
                continue;
            }

            WorkflowActivityStepState state = IsGroupComplete(group, completedExactly, completedVariants)
                ? WorkflowActivityStepState.Done
                : IsGroupStarted(group, completedExactly, completedVariants)
                    ? WorkflowActivityStepState.Active
                    : WorkflowActivityStepState.Pending;
            steps.Add(new WorkflowActivityProgressStep(label, state));
        }

        if (steps.TrueForAll(step => step.State is not WorkflowActivityStepState.Active))
        {
            ReactivateEarliestUnfinishedStep(steps, completedExactly, completedVariants);
        }

        int currentStep = steps.FindIndex(step => step.State is WorkflowActivityStepState.Active);
        if (currentStep < 0)
        {
            currentStep = Math.Max(0, steps.Count - 1);
        }

        return new WorkflowActivityProgress(currentStep, steps);
    }

    /// <summary>
    /// The continuity fallback for the moments the completion data proves nothing in flight —
    /// nothing has completed yet, or the only unfinished work sits in unlabeled housekeeping:
    /// the earliest unfinished group's step (an unlabeled group borrowing its nearest labeled
    /// neighbour) reads Active so the surface never goes blank, and deliberately not Done, so
    /// the done-count under-reports for a poll tick rather than overclaiming.
    /// </summary>
    private void ReactivateEarliestUnfinishedStep(
        List<WorkflowActivityProgressStep> steps,
        HashSet<string> completedExactly,
        List<string> completedVariants)
    {
        if (steps.Count == 0)
        {
            return;
        }

        int lastLabeledStep = -1;
        foreach (WorkflowActivityGroup group in Groups)
        {
            if (group.Label is not null)
            {
                lastLabeledStep++;
            }

            if (!IsGroupComplete(group, completedExactly, completedVariants))
            {
                int borrowed = Math.Max(0, lastLabeledStep);
                steps[borrowed] = steps[borrowed] with { State = WorkflowActivityStepState.Active };
                return;
            }
        }
    }

    private bool IsGroupComplete(
        WorkflowActivityGroup group,
        HashSet<string> completedExactly,
        List<string> completedVariants)
    {
        for (int index = group.FromIndex; index <= group.ToIndex && index < BlockIds.Count; index++)
        {
            if (BlockIds[index] is not string templateId)
            {
                continue;
            }

            if (!IsBlockComplete(templateId, completedExactly, completedVariants))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsGroupStarted(
        WorkflowActivityGroup group,
        HashSet<string> completedExactly,
        List<string> completedVariants)
    {
        for (int index = group.FromIndex; index <= group.ToIndex && index < BlockIds.Count; index++)
        {
            if (BlockIds[index] is string templateId
                && IsBlockComplete(templateId, completedExactly, completedVariants))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsBlockComplete(
        string templateId,
        HashSet<string> completedExactly,
        List<string> completedVariants)
    {
        if (completedExactly.Contains(templateId))
        {
            return true;
        }

        string variantPrefix = templateId + STABLE_ID_SUFFIX_SEPARATOR;
        return completedVariants.Any(completedId =>
            completedId.StartsWith(variantPrefix, StringComparison.Ordinal));
    }
}

/// <summary>
/// One contiguous run of blocks doing one job. <see cref="Label"/> is the line a client reads
/// while the group runs; null marks housekeeping that shows nothing of its own.
/// </summary>
public sealed record WorkflowActivityGroup(int FromIndex, int ToIndex, string? Label);

/// <summary>
/// A run's place in its script: the labeled steps in order, each carrying its state — Done when
/// its group's blocks all completed, Active when the group provably started and is unfinished
/// (several at once under the engine's parallel execution), Pending otherwise. CurrentStepIndex
/// is the first Active step, kept for readers that render a single line.
/// </summary>
public sealed record WorkflowActivityProgress(
    int CurrentStepIndex,
    IReadOnlyList<WorkflowActivityProgressStep> Steps)
{
    public IReadOnlyList<string> StepLabels => [.. Steps.Select(step => step.Label)];
}

public sealed record WorkflowActivityProgressStep(string Label, WorkflowActivityStepState State);

public enum WorkflowActivityStepState
{
    Done,
    Active,
    Pending,
}

public static class WorkflowActivityScriptSerializer
{
    private const int SCHEMA_VERSION = 1;

    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    public static string Serialize(WorkflowActivityScript script)
    {
        return JsonSerializer.Serialize(new StoredScript(SCHEMA_VERSION, script.Groups, script.BlockIds), _options);
    }

    /// <summary>
    /// The cached-decline sentinel: an empty script, stored so the ensurer stops paying for a
    /// model call the version has already declined. Resolves to zero steps, which every reader
    /// already treats as "no activity surface".
    /// </summary>
    public static string SerializeDeclined()
    {
        return Serialize(new WorkflowActivityScript([], []));
    }

    public static WorkflowActivityScript? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            StoredScript? stored = JsonSerializer.Deserialize<StoredScript>(json, _options);
            if (stored is null
                || stored.SchemaVersion != SCHEMA_VERSION
                || stored.Groups is null
                || stored.BlockIds is null
                || !GroupsPartitionBlocks(stored.Groups, stored.BlockIds.Count))
            {
                return null;
            }

            return new WorkflowActivityScript(stored.Groups, stored.BlockIds);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool GroupsPartitionBlocks(IReadOnlyList<WorkflowActivityGroup> groups, int blockCount)
    {
        int expectedStart = 0;
        foreach (WorkflowActivityGroup group in groups)
        {
            if (group.FromIndex != expectedStart || group.ToIndex < group.FromIndex || group.ToIndex >= blockCount)
            {
                return false;
            }

            expectedStart = group.ToIndex + 1;
        }

        return expectedStart == blockCount;
    }

    private sealed record StoredScript(
        int SchemaVersion,
        IReadOnlyList<WorkflowActivityGroup>? Groups,
        IReadOnlyList<string?>? BlockIds);
}

using IniaAi.Application.Common.Interfaces.Services;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The scalar metadata of one <c>WorkflowVersion</c> the planner needs — never the heavy payload, so
/// the keep/trim decision is made without loading any JSON.
/// </summary>
internal readonly record struct WorkflowVersionMetadata(
    string Id,
    string WorkflowTypeId,
    DateTime CreatedAt,
    string ContentHash,
    DateTime? PayloadTrimmedAt,
    bool HasPayload);

/// <summary>
/// Pure keep/trim decision for workflow-version payload retention. Given the scalar metadata of the
/// deployment's versions and a policy, it returns the ids whose payloads to clear — keeping the
/// head, the earliest, the last-N, and anything inside the time window of every workflow-type
/// line, oldest-first and capped at the per-run limit. Head selection matches the cross-instance
/// currency card's ordering exactly so the two never disagree on which row is current.
/// </summary>
internal static class WorkflowVersionRetentionPlanner
{
    internal const int MIN_KEEP_PAYLOADS = 5;

    internal static IReadOnlyList<string> Plan(
        IReadOnlyList<WorkflowVersionMetadata> versions,
        WorkflowVersionRetentionPolicy policy,
        DateTime nowUtc)
    {
        int effectiveKeepN = Math.Max(policy.KeepLastNPayloads, MIN_KEEP_PAYLOADS);
        DateTime cutoff = nowUtc.AddDays(-policy.KeepNewerThanDays);

        var candidates = new List<WorkflowVersionMetadata>();
        foreach (IGrouping<string, WorkflowVersionMetadata> typeGroup in versions.GroupBy(version => version.WorkflowTypeId))
        {
            List<WorkflowVersionMetadata> oldestFirst = [.. typeGroup
                .OrderBy(version => version.CreatedAt)
                .ThenBy(version => version.ContentHash, StringComparer.Ordinal)
                .ThenBy(version => version.Id, StringComparer.Ordinal)];

            HashSet<string> keep = BuildKeepSet(oldestFirst, effectiveKeepN, cutoff);

            candidates.AddRange(oldestFirst.Where(version =>
                version.HasPayload
                && version.PayloadTrimmedAt is null
                && !keep.Contains(version.Id)));
        }

        return
        [
            .. candidates
                .OrderBy(version => version.CreatedAt)
                .ThenBy(version => version.ContentHash, StringComparer.Ordinal)
                .ThenBy(version => version.Id, StringComparer.Ordinal)
                .Take(policy.MaxPayloadsTrimmedPerRun)
                .Select(version => version.Id)
        ];
    }

    private static HashSet<string> BuildKeepSet(
        List<WorkflowVersionMetadata> oldestFirst,
        int effectiveKeepN,
        DateTime cutoff)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal)
        {
            oldestFirst[^1].Id,
            oldestFirst[0].Id,
        };

        for (int i = Math.Max(0, oldestFirst.Count - effectiveKeepN); i < oldestFirst.Count; i++)
        {
            _ = keep.Add(oldestFirst[i].Id);
        }

        foreach (WorkflowVersionMetadata version in oldestFirst)
        {
            if (version.CreatedAt >= cutoff)
            {
                _ = keep.Add(version.Id);
            }
        }

        return keep;
    }
}

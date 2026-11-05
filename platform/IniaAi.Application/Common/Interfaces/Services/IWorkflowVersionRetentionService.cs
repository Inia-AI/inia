namespace IniaAi.Application.Common.Interfaces.Services;

/// <summary>
/// The window that decides which <c>WorkflowVersion</c> payloads to keep. A version's heavy JSON is
/// retained if the version is among the last <see cref="KeepLastNPayloads"/> of its workflow type
/// OR newer than <see cref="KeepNewerThanDays"/> days OR an always-keep invariant (the current
/// head and the earliest version). Everything else has its JSON cleared while the lightweight
/// metadata row — content hash, lineage pointer, summary — is kept, so version history and
/// cross-instance comparison stay correct. <see cref="ReportOnly"/> computes the plan without
/// mutating anything. <see cref="MaxPayloadsTrimmedPerRun"/> caps how much one run clears so a first
/// run over a large backlog drains gradually instead of in one heavy sweep.
/// </summary>
public record WorkflowVersionRetentionPolicy(
    int KeepLastNPayloads,
    int KeepNewerThanDays,
    bool ReportOnly,
    int MaxPayloadsTrimmedPerRun);

/// <summary>
/// Outcome of a payload-trim run. In <see cref="WorkflowVersionRetentionPolicy.ReportOnly"/> mode the
/// counts are what <em>would</em> be cleared; nothing was changed.
/// </summary>
public record PayloadTrimResult(
    int PayloadsTrimmed,
    long BytesReclaimedEstimate,
    bool WasReportOnly,
    IReadOnlyList<string> Errors);

/// <summary>
/// Clears the stored JSON of old <c>WorkflowVersion</c> rows to reclaim storage while keeping every
/// row — and therefore every content hash and lineage pointer — so version history, provenance, and
/// cross-instance comparison remain correct. It never deletes a row.
/// </summary>
public interface IWorkflowVersionRetentionService
{
    Task<PayloadTrimResult> TrimAsync(
        WorkflowVersionRetentionPolicy policy,
        CancellationToken ct = default);
}

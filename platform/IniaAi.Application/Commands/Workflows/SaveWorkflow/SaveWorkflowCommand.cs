using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Signatures;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.Workflows.SaveWorkflow;

/// <summary>
/// Saves a workflow template as the current Core version: swaps the old template row for the
/// new one, appends a content-hashed <see cref="WorkflowVersion"/> snapshot (deduped against
/// the prior head), and syncs signature overrides with the workflow-level field definitions
/// (renames carry rows to the new name, deletions cascade) — all in one transaction.
/// </summary>
/// <param name="BaseVersionId">The head version the caller based its edit on; only checked when <paramref name="EnforceOptimisticConcurrency"/> is true.</param>
/// <param name="EnforceOptimisticConcurrency">When true, the save is rejected with a conflict unless <paramref name="BaseVersionId"/> matches the current head.</param>
/// <param name="SignatureFieldRenames">Field renames performed in the editor session being saved, in the order they happened, so client override rows follow the new names.</param>
/// <param name="ConfirmSignatureFieldDeletions">Explicit acknowledgement that this save may delete signature fields holding client override/pending rows (across organizations). Without it, such a save is rejected with <c>Signatures.DeletionRequiresConfirmation</c> listing the affected fields — uploads, version restores, and API saves reach the delete path without ever seeing the editor's confirmation dialog.</param>
/// <param name="VersionNote">A note stored on the new version beside its generated summary, such as how many of the saved changes the editor agent proposed.</param>
/// <param name="SkipAuthorGates">Skips the author gates: the agent-block and screening gates, and the completeness rules a closed module puts on its own settings (a rule on who may change a setting still applies). Only a system move that changes neither blocks nor those settings sets it, such as the startup migration of overview prompts: the gates judge an author's edit, and the template's next author save still meets them.</param>
public record SaveWorkflowCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    Workflow Workflow,
    string? BaseVersionId = null,
    bool EnforceOptimisticConcurrency = false,
    IReadOnlyList<SignatureFieldRename>? SignatureFieldRenames = null,
    bool ConfirmSignatureFieldDeletions = false,
    string? VersionNote = null,
    bool SkipAuthorGates = false) : IRequest<ErrorOr<SaveWorkflowResult>>;

/// <param name="VersionId">The head version after the save — a new snapshot, or the prior head when deduped.</param>
/// <param name="ContentHash">The canonical content hash of the saved template.</param>
/// <param name="WasDeduped">True when the save matched the prior head and appended no new snapshot.</param>
/// <param name="SignatureSync">Signature-override sync bookkeeping, for the post-save toast.</param>
/// <param name="Warnings">Non-fatal caveats the author should see (e.g. agent fuse values accepted unchecked because the runtime's limits were unknown). Null or empty when the save was fully checked.</param>
public record SaveWorkflowResult(
    string VersionId,
    string ContentHash,
    bool WasDeduped,
    SignatureSyncResult SignatureSync,
    IReadOnlyList<string>? Warnings = null);

using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.Signatures.SweepPendingSignatureOverrides;

/// <summary>
/// Re-evaluates every outstanding <c>PendingSignatureOverride</c> for an organisation
/// against the tenant's current <c>SignatureReviewThreshold</c>. Triggered when the
/// threshold is lowered so pendings that already satisfy the new bar (but didn't have
/// a fresh vote to trigger their own promotion) get promoted / cancelled immediately.
/// Returns the counts of each outcome for a UX summary.
/// </summary>
public record SweepPendingSignatureOverridesCommand(
    string OrganizationId,
    string UserId) : IRequest<ErrorOr<SweepPendingSignatureOverridesResult>>;

/// <summary>
/// Outcome summary of a sweep run. <see cref="SkippedStale"/> counts pendings whose
/// approve count met the threshold but whose target block is no longer signature-
/// bearing (block was deleted or its properties were swapped). The sweep silently
/// leaves those pendings in place rather than promote-to-nothing or cancel-without-
/// rejection; surfacing the count lets the caller distinguish "still being reviewed"
/// (<see cref="StillPending"/>) from "stuck because the workflow moved on".
/// </summary>
public record SweepPendingSignatureOverridesResult(
    int Promoted,
    int Cancelled,
    int StillPending,
    int SkippedStale);

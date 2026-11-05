using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Entities.SharedComponents.Enums;

namespace IniaAi.Application.Commands.SharedComponents.ApplySharedComponentReconcile;

/// <summary>
/// The write half of the reconcile pair: one batch of per-instance decisions against one anchor
/// template, applied atomically with every fan-out they trigger. <c>BaseVersionId</c> is the
/// anchor's concurrency token, enforced by the anchor's save; each head-consuming decision
/// (UpdateShared, AdoptHead, Relink) additionally echoes the component head it was decided
/// against via <c>ExpectedHeadVersionId</c>. An UpdateShared pushes the instance's
/// reverse-normalized content as the component's new head and rewrites every Linked consumer in
/// the same transaction — instances omitted from the decisions are still reached by that fan-out.
/// Decisions that would orphan signature overrides (removed slots delete their blocks' rows;
/// dropped placeholders leave rows inert) are refused with
/// <see cref="SharedComponentErrorCodes.ORPHANS_NEED_ACKNOWLEDGEMENT"/> until re-sent with
/// <see cref="SharedComponentReconcileDecision.AcknowledgeSignatureOrphans"/>.
/// </summary>
public record ApplySharedComponentReconcileCommand(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string BaseVersionId,
    IReadOnlyList<SharedComponentReconcileDecision> Decisions)
    : IRequest<ErrorOr<SharedComponentReconcileApplyResult>>;

/// <summary>
/// One instance-level decision. The instance is addressed by exactly one of
/// <paramref name="TargetStableId"/> or <paramref name="TargetPath"/>;
/// <paramref name="SharedComponentId"/> is a cross-checked echo of the component the caller
/// believes the instance is a copy of. Already-satisfied transitions (breaking out a broken-out
/// copy, relinking a linked one) are idempotent no-ops. A decision scoped to a single entry of a
/// section copy additionally carries exactly one of <paramref name="EntryKey"/> or
/// <paramref name="EntryStableId"/> (both null = whole-section); a ref-entry push or adopt echoes
/// the referenced Block component's head via <paramref name="ExpectedRefHeadVersionId"/>.
/// </summary>
public sealed record SharedComponentReconcileDecision(
    string SharedComponentId,
    string? TargetStableId,
    string? TargetPath,
    SharedComponentReconcileAction Action,
    string? ExpectedHeadVersionId = null,
    bool AcknowledgeSignatureOrphans = false,
    string? EntryKey = null,
    string? EntryStableId = null,
    string? ExpectedRefHeadVersionId = null,
    bool AcknowledgeOverwrittenEntries = false);

/// <param name="AlreadyCurrent">
/// True when an UpdateShared found the derived definition already byte-identical to the head —
/// nothing was appended and no fan-out ran for it.
/// </param>
public sealed record SharedComponentReconcileDecisionOutcome(
    string SharedComponentId,
    string TargetPath,
    string? TargetStableId,
    SharedComponentReconcileAction Action,
    bool AlreadyCurrent);

/// <param name="Appended">True when this apply created a new head version for the component.</param>
public sealed record SharedComponentReconciledHead(
    string SharedComponentId,
    string HeadVersionId,
    string HeadContentHash,
    bool Appended);

public sealed record SharedComponentReconcileApplyResult(
    string WorkflowTypeId,
    IReadOnlyList<SharedComponentReconcileDecisionOutcome> DecisionOutcomes,
    IReadOnlyList<SharedComponentConsumerSaveResult> WorkflowSaves,
    IReadOnlyList<SharedComponentReconciledHead> ComponentHeads);

public class ApplySharedComponentReconcileCommandHandler
    : IRequestHandler<ApplySharedComponentReconcileCommand, ErrorOr<SharedComponentReconcileApplyResult>>
{
    private readonly SharedComponentFanOutService _fanOutService;

    public ApplySharedComponentReconcileCommandHandler(SharedComponentFanOutService fanOutService)
    {
        _fanOutService = fanOutService ?? throw new ArgumentNullException(nameof(fanOutService));
    }

    public async Task<ErrorOr<SharedComponentReconcileApplyResult>> Handle(
        ApplySharedComponentReconcileCommand request,
        CancellationToken cancellationToken)
    {
        return await _fanOutService.ApplyReconcileAsync(request, cancellationToken);
    }
}

public class ApplySharedComponentReconcileCommandValidator
    : AbstractValidator<ApplySharedComponentReconcileCommand>
{
    public ApplySharedComponentReconcileCommandValidator()
    {
        _ = RuleFor(x => x.OrganizationId).NotEmpty();
        _ = RuleFor(x => x.UserId).NotEmpty();
        _ = RuleFor(x => x.WorkflowTypeId).NotEmpty();
        _ = RuleFor(x => x.BaseVersionId).NotEmpty();
        _ = RuleFor(x => x.Decisions).NotEmpty();
        _ = RuleForEach(x => x.Decisions).ChildRules(decision =>
        {
            _ = decision.RuleFor(d => d.SharedComponentId).NotEmpty();
            _ = decision.RuleFor(d => d.Action).IsInEnum();
        });
    }
}

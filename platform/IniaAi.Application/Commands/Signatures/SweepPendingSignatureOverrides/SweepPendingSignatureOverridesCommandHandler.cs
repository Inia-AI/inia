using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Specifications.Signatures;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Signatures.SweepPendingSignatureOverrides;

/// <summary>
/// Sweeps every outstanding pending override for an organisation and resolves any whose
/// vote counts already satisfy the tenant's current threshold. Intended as the follow-up
/// step when the threshold is lowered — the normal CastSignatureOverrideVote promotion
/// path only fires on a fresh vote, so pendings that already had enough approvals before
/// the change would sit forever without this sweep.
/// </summary>
public class SweepPendingSignatureOverridesCommandHandler
    : IRequestHandler<SweepPendingSignatureOverridesCommand, ErrorOr<SweepPendingSignatureOverridesResult>>
{
    private const int MIN_THRESHOLD = 2;

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ILogger<SweepPendingSignatureOverridesCommandHandler> _logger;

    public SweepPendingSignatureOverridesCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IUserDataUnitOfWork userDataUnitOfWork,
        ILogger<SweepPendingSignatureOverridesCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<SweepPendingSignatureOverridesResult>> Handle(
        SweepPendingSignatureOverridesCommand request,
        CancellationToken cancellationToken)
    {
        int threshold = await LoadTenantThresholdAsync(request.OrganizationId, cancellationToken);

        IRepository<PendingSignatureOverride> pendingRepo =
            _coreUnitOfWork.Repository<PendingSignatureOverride>();

        List<PendingSignatureOverride> pendings = await pendingRepo
            .GetAll()
            .Where(p => p.OrganizationId == request.OrganizationId)
            .Include(p => p.Votes)
            .ToListAsync(cancellationToken);

        int promoted = 0;
        int cancelled = 0;
        int skippedStale = 0;

        // Phase 1: classify every pending so phase 2 can batch one user-email
        // query for every actor across every promotion. Without this, the
        // resolver would be invoked per pending (N+1 against the user table).
        // Mirrors the Cast-vote tie rule: approves ≥ threshold promotes,
        // regardless of rejects.
        List<(PendingSignatureOverride Pending, List<string> ApproverIds)> promoteCandidates = [];
        List<PendingSignatureOverride> cancelCandidates = [];

        foreach (PendingSignatureOverride pending in pendings)
        {
            int approveCount = 0;
            int rejectCount = 0;
            List<string> approverIds = [];
            foreach (SignatureOverrideVote vote in pending.Votes)
            {
                if (vote.VoteKind is SignatureVoteKind.Approve)
                {
                    approveCount++;
                    approverIds.Add(vote.VoterUserId);
                    continue;
                }

                rejectCount++;
            }

            if (approveCount >= threshold)
            {
                promoteCandidates.Add((pending, approverIds));
                continue;
            }

            if (rejectCount >= threshold)
            {
                cancelCandidates.Add(pending);
            }
        }

        // Phase 2: one batched WHERE Id IN (...) for every actor (proposer +
        // every approver) across every promotion candidate.
        HashSet<string> idsToResolve = [];
        foreach ((PendingSignatureOverride pending, List<string> approverIds) in promoteCandidates)
        {
            _ = idsToResolve.Add(pending.ProposerUserId);
            foreach (string id in approverIds)
            {
                _ = idsToResolve.Add(id);
            }
        }

        Dictionary<string, string?> emails = await SignatureActorResolver.ResolveEmailsAsync(
            _userDataUnitOfWork,
            idsToResolve,
            cancellationToken);

        // Phase 3: batched field-validity check.
        //
        // Defense-in-depth staleness check (mirrors Cast-vote's PromoteAsync): the
        // normal editor flow deletes a removed field's pendings inside the save
        // transaction, but we verify anyway before writing an override row for a
        // potentially-gone field.
        //
        // We group promote candidates by WorkflowTypeId, load each distinct workflow
        // (with definitions, blocks + child sections) ONCE, then check per pending that
        // the field is still defined and referenced by at least one block template.
        Dictionary<string, Dictionary<string, List<string>>> referencedFieldsByWorkflowType =
            await LoadReferencedFieldsAsync(promoteCandidates, cancellationToken);

        // Phase 4: write each promotion's override + audit row.
        foreach ((PendingSignatureOverride pending, List<string> approverIds) in promoteCandidates)
        {
            bool fieldStillValid =
                referencedFieldsByWorkflowType.TryGetValue(pending.WorkflowTypeId, out Dictionary<string, List<string>>? fields)
                && fields.TryGetValue(pending.PlaceholderName, out List<string>? referencingBlockNames)
                && referencingBlockNames.Count > 0;
            if (!fieldStillValid)
            {
                _logger.LogWarning(
                    "Sweep skipped promotion for pending {PendingId}: field {Placeholder} is no longer defined and referenced.",
                    pending.Id,
                    pending.PlaceholderName);
                skippedStale++;
                continue;
            }

            emails.TryGetValue(pending.ProposerUserId, out string? proposerEmail);

            List<SignatureAuditApprover> approvers = [];
            foreach (string id in approverIds)
            {
                emails.TryGetValue(id, out string? email);
                approvers.Add(new SignatureAuditApprover(id, email));
            }

            string blockNamesJson = SignatureOverrideWriter.SerializeBlockNames(
                referencedFieldsByWorkflowType[pending.WorkflowTypeId][pending.PlaceholderName]);

            await SignatureOverrideWriter.ApplyUpdatesAsync(
                pending.OrganizationId,
                pending.WorkflowTypeId,
                pending.ProposerUserId,
                proposerEmail,
                approvers,
                new[] { new SignatureFieldUpdate(pending.PlaceholderName, pending.ProposedValue, blockNamesJson) },
                _coreUnitOfWork,
                cancellationToken);
            pendingRepo.HardRemove(pending);
            promoted++;
        }

        // Phase 4: cancel rejected pendings.
        foreach (PendingSignatureOverride pending in cancelCandidates)
        {
            pendingRepo.HardRemove(pending);
            cancelled++;
        }

        if (promoted > 0 || cancelled > 0)
        {
            _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);
        }

        // Stale candidates are deliberately NOT bucketed into stillPending — they
        // weren't promoted, weren't cancelled, and are blocked from ever being
        // either until the editor flow cleans the orphan up. Surfacing them as a
        // separate count keeps the caller's UX summary honest.
        int stillPending = pendings.Count - promoted - cancelled - skippedStale;

        _logger.LogInformation(
            "Signature-override sweep for org {Org} at threshold {Threshold}: promoted={Promoted} cancelled={Cancelled} stillPending={Still} skippedStale={Stale}",
            request.OrganizationId,
            threshold,
            promoted,
            cancelled,
            stillPending,
            skippedStale);

        return new SweepPendingSignatureOverridesResult(promoted, cancelled, stillPending, skippedStale);
    }

    /// <summary>
    /// For every distinct <c>WorkflowTypeId</c> across the promote candidates, loads
    /// the canonical workflow once and projects its defined signature fields to the
    /// names of the blocks whose templates reference them (case-insensitive field
    /// keys). The result is consumed by the Phase-3/4 loop via dictionary lookups per
    /// pending, replacing what used to be one workflow load per pending.
    ///
    /// <para>
    /// A workflow type missing from the dictionary means the workflow row no longer
    /// exists — those pendings count as <c>skippedStale</c>. A field missing from the
    /// per-workflow map (or referenced by zero blocks) means it was deleted or renamed
    /// out from under the pending — same outcome.
    /// </para>
    /// </summary>
    private async Task<Dictionary<string, Dictionary<string, List<string>>>> LoadReferencedFieldsAsync(
        IReadOnlyList<(PendingSignatureOverride Pending, List<string> ApproverIds)> promoteCandidates,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Dictionary<string, List<string>>> result = [];
        if (promoteCandidates.Count == 0)
        {
            return result;
        }

        HashSet<string> distinctWorkflowTypeIds = [];
        foreach ((PendingSignatureOverride pending, List<string> _) in promoteCandidates)
        {
            _ = distinctWorkflowTypeIds.Add(pending.WorkflowTypeId);
        }

        foreach (string workflowTypeId in distinctWorkflowTypeIds)
        {
            var spec = new WorkflowWithSectionsAndBlocksByTypeSpec(workflowTypeId);
            Workflow? workflow = await _coreUnitOfWork.Repository<Workflow>()
                .GetFirstOrDefaultAsync(spec, cancellationToken);

            if (workflow is null)
            {
                continue;
            }

            var fieldMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, List<Domain.Entities.Workflows.Blocks.Block> blocks)
                in workflow.GetSignatureFieldReferences())
            {
                fieldMap[name] = [.. blocks.Select(b => b.Name)];
            }

            result[workflowTypeId] = fieldMap;
        }

        return result;
    }

    private async Task<int> LoadTenantThresholdAsync(string organizationId, CancellationToken cancellationToken)
    {
        Organization? organization = await _userDataUnitOfWork.Repository<Organization>()
            .GetAll()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);

        if (organization is null || organization.SignatureReviewThreshold < MIN_THRESHOLD)
        {
            return MIN_THRESHOLD;
        }

        return organization.SignatureReviewThreshold;
    }
}

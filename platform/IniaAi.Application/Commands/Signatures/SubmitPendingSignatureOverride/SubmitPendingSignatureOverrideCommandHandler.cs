using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Specifications.Signatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.Signatures.SubmitPendingSignatureOverride;

/// <summary>
/// Creates a new <see cref="PendingSignatureOverride"/> for review. If a pending
/// already exists for the same <c>(OrganizationId, WorkflowTypeId, PlaceholderName)</c>
/// tuple it is deleted first (cascade clears votes) so there is always at most one
/// pending per field. The proposer's submission is seeded as an Approve vote so the
/// tenant threshold counts it; the proposer cannot vote again afterwards (see
/// <c>CastSignatureOverrideVoteCommand</c>).
///
/// <para>
/// Staleness: an unknown field name is a validation error; a defined field whose
/// token no block template references anymore returns 409 <c>Signatures.Stale</c>
/// (the client raced a staff rename/delete and must refresh its view).
/// </para>
/// </summary>
public class SubmitPendingSignatureOverrideCommandHandler
    : IRequestHandler<SubmitPendingSignatureOverrideCommand, ErrorOr<string>>
{
    private const string ERROR_WORKFLOW_NOT_FOUND = "Workflow.NotFound";
    private const string ERROR_SIGNATURES_STALE = "Signatures.Stale";
    private const string ERROR_SIGNATURES_UNKNOWN_FIELD = "Signatures.UnknownField";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ILogger<SubmitPendingSignatureOverrideCommandHandler> _logger;

    public SubmitPendingSignatureOverrideCommandHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ILogger<SubmitPendingSignatureOverrideCommandHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<string>> Handle(
        SubmitPendingSignatureOverrideCommand request,
        CancellationToken cancellationToken)
    {
        bool hasAccess = await _coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(
                a => a.WorkflowTypeId == request.WorkflowTypeId
                    && a.OrganizationId == request.OrganizationId,
                cancellationToken);

        if (!hasAccess)
        {
            return Error.NotFound(
                ERROR_WORKFLOW_NOT_FOUND,
                $"No workflow of type '{request.WorkflowTypeId}' was found for this organisation.");
        }

        // Workflow definition is shared across tenants — access is gated above via
        // WorkflowTypeOrganizationAccess. Per-tenant scoping for overrides is enforced
        // separately on the PendingSignatureOverride / SignatureFieldOverride rows.
        // Spec applies OrderByDescending(CreatedAt) so we always resolve the same
        // canonical row that the workflow execution path uses.
        var workflowSpec = new WorkflowWithSectionsAndBlocksByTypeSpec(request.WorkflowTypeId);
        Workflow? workflow = await _coreUnitOfWork.Repository<Workflow>()
            .GetFirstOrDefaultAsync(workflowSpec, cancellationToken);

        if (workflow is null)
        {
            return Error.NotFound(
                ERROR_WORKFLOW_NOT_FOUND,
                $"No workflow of type '{request.WorkflowTypeId}' was found for this organisation.");
        }

        // Rows are stored under the definition's exact casing so the case-sensitive DB key
        // (unique index, rename/delete lookups) always matches, whatever casing the client sent.
        WorkflowSignatureField? definition = workflow.SignatureFields.FirstOrDefault(f =>
            string.Equals(f.Name, request.PlaceholderName, StringComparison.OrdinalIgnoreCase));
        if (definition is null)
        {
            return Error.Validation(
                ERROR_SIGNATURES_UNKNOWN_FIELD,
                $"'{request.PlaceholderName}' is not a defined signature field on this workflow.");
        }

        string placeholderName = definition.Name;

        bool referenced = workflow.GetSignatureBearingBlocks().Any(b =>
            b.Properties.HasPlaceholder(
                Workflow.SignatureFieldToken(placeholderName), StringComparison.OrdinalIgnoreCase));
        if (!referenced)
        {
            _logger.LogInformation(
                "Rejected pending signature submission on workflow type {WorkflowTypeId} — field {Placeholder} referenced by no block",
                LogSanitizer.ForLog(request.WorkflowTypeId),
                placeholderName);
            return Error.Conflict(
                ERROR_SIGNATURES_STALE,
                $"Signature field '{placeholderName}' is no longer referenced by any block — the client view is out of date and must be refreshed.");
        }

        IRepository<PendingSignatureOverride> pendingRepo =
            _coreUnitOfWork.Repository<PendingSignatureOverride>();

        string loweredName = placeholderName.ToLowerInvariant();
        PendingSignatureOverride? existing = await pendingRepo
            .GetAll()
            .Where(p => p.OrganizationId == request.OrganizationId
                && p.WorkflowTypeId == request.WorkflowTypeId
                && p.PlaceholderName.ToLower() == loweredName)
            .Include(p => p.Votes)
            .FirstOrDefaultAsync(cancellationToken);

        if (existing is not null)
        {
            pendingRepo.HardRemove(existing);
        }

        var pending = new PendingSignatureOverride
        {
            OrganizationId = request.OrganizationId,
            WorkflowTypeId = request.WorkflowTypeId,
            PlaceholderName = placeholderName,
            ProposedValue = request.ProposedValue,
            ProposerUserId = request.UserId,
        };

        await pendingRepo.AddAsync(pending);

        var seedVote = new SignatureOverrideVote
        {
            PendingOverrideId = pending.Id,
            VoterUserId = request.UserId,
            VoteKind = SignatureVoteKind.Approve,
        };

        await _coreUnitOfWork.Repository<SignatureOverrideVote>().AddAsync(seedVote);

        _ = await _coreUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation(
            "Created pending signature override {PendingId} for field {Placeholder} on workflow type {WorkflowTypeId}",
            pending.Id,
            placeholderName,
            LogSanitizer.ForLog(request.WorkflowTypeId));

        return pending.Id;
    }
}

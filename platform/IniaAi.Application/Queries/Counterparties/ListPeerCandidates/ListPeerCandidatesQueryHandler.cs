using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.Counterparties.ListPeerCandidates;

/// <summary>
/// Answers what the peer picker may offer, with the <see cref="IPeerCandidateJudge"/> verdict on
/// each counterparty's stored financials.
/// </summary>
public class ListPeerCandidatesQueryHandler
    : IRequestHandler<ListPeerCandidatesQuery, ErrorOr<List<PeerCandidateDto>>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IPeerCandidateJudge _judge;

    public ListPeerCandidatesQueryHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork,
        IPeerCandidateJudge judge)
    {
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _judge = judge ?? throw new ArgumentNullException(nameof(judge));
    }

    public async Task<ErrorOr<List<PeerCandidateDto>>> Handle(
        ListPeerCandidatesQuery request,
        CancellationToken cancellationToken)
    {
        PeerWorkflowContext workflowContext = PeerWorkflowContext.None;
        if (!string.IsNullOrWhiteSpace(request.WorkflowTypeId))
        {
            ErrorOr<PeerWorkflowContext> resolved = await ResolveWorkflowContextAsync(
                request.OrganizationId,
                request.WorkflowTypeId,
                cancellationToken);

            if (resolved.IsError)
            {
                return resolved.Errors;
            }

            workflowContext = resolved.Value;
        }

        IQueryable<Counterparty> query = _userDataUnitOfWork.Repository<Counterparty>()
            .GetAll()
            .AsNoTracking()
            .Where(counterparty => counterparty.OrganizationId == request.OrganizationId);

        if (workflowContext.CounterpartyTypeIds.Count > 0)
        {
            // A counterparty of a type the workflow does not accept is not a peer of that
            // workflow at all — hidden outright, where a right-type counterparty lacking data
            // stays listed with its reason. Same membership rule as the counterparty picker
            // (ListCounterpartiesQueryHandler), so the two controls on one form agree.
            IReadOnlyList<string> mappedTypeIds = workflowContext.CounterpartyTypeIds;
            query = query.Where(counterparty =>
                counterparty.CounterpartyTypes.Any(type => mappedTypeIds.Contains(type.Id)));
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // Lowered on both sides: string.Contains translates to a case-SENSITIVE strpos on
            // Npgsql and a collation-dependent CHARINDEX on SQL Server, so an unlowered search
            // would match different counterparties on different deployments.
            string search = request.Search.Trim().ToLowerInvariant();
            query = query.Where(counterparty =>
                counterparty.Name.ToLower().Contains(search)
                || counterparty.NameVariants.ToLower().Contains(search));
        }

        List<Counterparty> counterparties = await query
            .OrderBy(counterparty => counterparty.Name)
            .ThenBy(counterparty => counterparty.Id)
            .Skip(request.Skip)
            .Take(request.Top)
            .ToListAsync(cancellationToken);

        return counterparties.ConvertAll(counterparty =>
            ToCandidate(counterparty, _judge.Judge(counterparty, workflowContext.PeerDataPath)));
    }

    /// <summary>
    /// What the chosen workflow contributes to the candidate query: where its Peer Financials
    /// block reads stored figures from, and which counterparty types it accepts — the same
    /// <c>CounterpartyTypeMappings</c> the counterparty picker filters on, so both controls
    /// derive their population from one source. An empty type list means the workflow maps no
    /// types and every type is offered, exactly as the counterparty picker behaves.
    /// </summary>
    private sealed record PeerWorkflowContext(string PeerDataPath, IReadOnlyList<string> CounterpartyTypeIds)
    {
        public static PeerWorkflowContext None { get; } = new(string.Empty, []);
    }

    private static PeerCandidateDto ToCandidate(Counterparty counterparty, PeerCandidateVerdict verdict)
    {
        return new PeerCandidateDto
        {
            Id = counterparty.Id,
            Name = counterparty.Name,
            NameVariants = counterparty.NameVariants,
            ReportedName = verdict.ReportedName,
            IsEligible = verdict.IsEligible,
            Availability = verdict.Availability,
            Currency = verdict.Currency,
            LatestPeriodEnd = verdict.LatestPeriodEnd,
        };
    }

    private async Task<ErrorOr<PeerWorkflowContext>> ResolveWorkflowContextAsync(
        string organizationId,
        string workflowTypeId,
        CancellationToken cancellationToken)
    {
        bool hasAccess = await WorkflowTypeAccessGuard.HasAccessAsync(
            _coreUnitOfWork, organizationId, workflowTypeId, cancellationToken);

        if (!hasAccess)
        {
            return Error.NotFound(
                "Workflow.NotFound",
                $"Workflow for type '{workflowTypeId}' was not found or is not accessible.");
        }

        // Ordered like CoreWorkflowRepository.GetByTypeIdAsync: legacy duplicate rows exist, and
        // judging eligibility against a different row than the run uses is the drift this filter
        // exists to prevent.
        Workflow? workflow = await _coreUnitOfWork.Repository<Workflow>()
            .GetAll()
            .AsNoTracking()
            .Where(candidate => candidate.WorkflowTypeId == workflowTypeId)
            .OrderByDescending(candidate => candidate.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (workflow is null)
        {
            return Error.NotFound(
                "Workflow.NotFound",
                $"Workflow for type '{workflowTypeId}' was not found or is not accessible.");
        }

        List<Block> peerBlocks = await _coreUnitOfWork.Repository<Block>()
            .GetAll()
            .AsNoTracking()
            .Where(block =>
                block.Section != null
                && block.Section.WorkflowId == workflow.Id
                && block.Type == BlockTypes.PeerFinancials)
            .ToListAsync(cancellationToken);

        string peerDataPath = peerBlocks
            .Select(block => block.Properties as PeerFinancialsProperties)
            .FirstOrDefault(properties => properties is not null)
            ?.PeerDataPath ?? string.Empty;

        List<string> counterpartyTypeIds = await _coreUnitOfWork
            .Repository<WorkflowTypeCounterpartyTypeMap>()
            .GetAll()
            .AsNoTracking()
            .Where(mapping => mapping.WorkflowTypeId == workflowTypeId)
            .Select(mapping => mapping.CounterpartyTypeId)
            .ToListAsync(cancellationToken);

        return new PeerWorkflowContext(peerDataPath, counterpartyTypeIds);
    }
}

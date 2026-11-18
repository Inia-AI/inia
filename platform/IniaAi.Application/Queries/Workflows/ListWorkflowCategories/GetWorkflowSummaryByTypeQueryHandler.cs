using ErrorOr;
using IniaAi.Application.Common.CreditCosts;
using IniaAi.Application.Common.RunLimits;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Common.Constants;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Enums;
using IniaAi.WebApi.Contracts.Dto;
using IniaAi.WebApi.Models.Dto;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.Workflows.ListWorkflowCategories;

/// <summary>
/// Handles the query to retrieve a workflow summary by workflow type ID.
/// Loads Variables, DocumentClasses, and CounterpartyTypeMappings for the CreateWorkflow page.
/// </summary>
public class GetWorkflowSummaryByTypeQueryHandler
    : IRequestHandler<GetWorkflowSummaryByTypeQuery, ErrorOr<WorkflowDto>>
{
    /// <summary>
    /// Search term for detecting Counterparty placeholders in block Properties JSON.
    /// Includes the opening brace to avoid false positives (e.g., "CounterpartyType").
    /// Matches: {Counterparty}, {Counterparty.Name}, {{Counterparty}}, etc.
    /// </summary>
    private const string COUNTERPARTY_PLACEHOLDER_SEARCH = "{" + SharedInputKeys.COUNTERPARTY;

    /// <summary>
    /// Search term for detecting PeerCompanies placeholders in block Properties JSON.
    /// Matches: {PeerCompanies}, {PeerCompanies[].Name}, {PeerCompanies[1].Name}, etc.
    /// </summary>
    private const string PEER_COMPANIES_PLACEHOLDER_SEARCH = "{" + SharedInputKeys.PEER_COMPANIES;

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly WorkflowTypeAccess _access;
    private readonly IWorkflowCreditCost _creditCost;
    private readonly IWorkflowPageLimit _pageLimit;

    public GetWorkflowSummaryByTypeQueryHandler(ICoreUnitOfWork coreUnitOfWork, WorkflowTypeAccess access, IWorkflowCreditCost creditCost, IWorkflowPageLimit pageLimit)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _creditCost = creditCost ?? throw new ArgumentNullException(nameof(creditCost));
        _pageLimit = pageLimit ?? throw new ArgumentNullException(nameof(pageLimit));
    }

    public async Task<ErrorOr<WorkflowDto>> Handle(
        GetWorkflowSummaryByTypeQuery request,
        CancellationToken cancellationToken)
    {
        WorkflowSummaryProjections.WorkflowParts? workflow = await _access.CanReachAsync(request.OrganizationId, request.WorkflowTypeId, cancellationToken)
            ? await _coreUnitOfWork.Repository<Workflow>()
                .GetAll()
                .AsNoTracking()
                .Where(w =>
                    w.WorkflowTypeId == request.WorkflowTypeId &&
                    w.WorkflowType != null &&
                    w.WorkflowTypeId != WorkflowType.GENERIC &&
                    w.WorkflowTypeId != WorkflowType.TEST_WORKFLOW_TYPE)
                .Select(WorkflowSummaryProjections.ToParts)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        if (workflow is null)
        {
            return Error.NotFound("Workflow.NotFound", $"Workflow for type '{request.WorkflowTypeId}' was not found or is not accessible.");
        }

        WorkflowDto result = WorkflowSummaryProjections.ToDto(workflow, _creditCost, _pageLimit);
        result.DocumentClasses = await WithMadeByAsync(result.DocumentClasses, workflow.DocumentClasses, request.OrganizationId, cancellationToken);

        // Check if any block in this workflow or its sections uses a Counterparty placeholder.
        // Checks the Properties JSON column (via double-cast trick for value-converted jsonb/nvarchar)
        result.IsCounterpartyRequired = await _coreUnitOfWork.Repository<Block>()
            .GetAll()
            .AsNoTracking()
            .Where(b => b.Section != null && b.Section.WorkflowId == workflow.Id)
            .AnyAsync(
                b => b.Properties != null && ((string)(object)b.Properties!).Contains(COUNTERPARTY_PLACEHOLDER_SEARCH),
                cancellationToken);

        // Peers reach a run by two independent routes and either is sufficient: a Peer Financials
        // block, which reads the selected peers' stored financials, or a PeerCompanies placeholder,
        // which any block's prompt may use to name them. A workflow that compares against peers
        // purely in prose carries no peer block, so block type alone would hide its picker.
        result.SupportsPeerCompanies = await _coreUnitOfWork.Repository<Block>()
            .GetAll()
            .AsNoTracking()
            .Where(b => b.Section != null && b.Section.WorkflowId == workflow.Id)
            .AnyAsync(
                b => b.Type == BlockTypes.PeerFinancials
                    || (b.Properties != null && ((string)(object)b.Properties!).Contains(PEER_COMPANIES_PLACEHOLDER_SEARCH)),
                cancellationToken);

        return result;
    }

    /// <summary>Names, on each input made by another workflow, that workflow and its output, where it still makes it and the organization can reach it.</summary>
    private async Task<List<DocumentClassDto>> WithMadeByAsync(
        List<DocumentClassDto> shown,
        List<DocumentClass> documentClasses,
        string organizationId,
        CancellationToken cancellationToken)
    {
        if (!documentClasses.Any(documentClass => documentClass.IsMadeByAnotherWorkflow))
        {
            return shown;
        }

        IReadOnlyCollection<string> reachable = await _access.TypeIdsAsync(organizationId, cancellationToken);
        List<DocumentClassDto> named = [];
        foreach (DocumentClassDto documentClass in shown)
        {
            DocumentClass? input = documentClasses.FirstOrDefault(candidate => candidate.Key == documentClass.Key && candidate.IsMadeByAnotherWorkflow);
            MadeByChoice? output = input is null || !reachable.Contains(input.MadeByWorkflowTypeId!, StringComparer.Ordinal)
                ? null
                : await MadeByLinks.OutputAsync(_coreUnitOfWork, input.MadeByWorkflowTypeId!, input.MadeByDocumentClassKey!, cancellationToken);
            named.Add(output is null
                ? documentClass
                : documentClass with { MadeBy = new DocumentClassMadeByDto(output.WorkflowTypeId, output.WorkflowTypeName, output.DisplayName) });
        }

        return named;
    }
}

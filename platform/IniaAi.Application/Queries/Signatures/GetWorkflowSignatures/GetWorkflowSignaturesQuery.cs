using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Signatures;

namespace IniaAi.Application.Queries.Signatures.GetWorkflowSignatures;

/// <summary>
/// Returns the full signature surface for a workflow type — every AI Query block
/// across every child section that exposes staff-authored signature fields, merged
/// with the block's own SignatureFieldValues overrides. Scoped by the organisation's
/// current non-deleted workflow of the given type; survives Save-As-Core id churn.
/// </summary>
public record GetWorkflowSignaturesQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId) : IRequest<ErrorOr<WorkflowSignaturesDto>>;

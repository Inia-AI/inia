using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Signatures;

namespace IniaAi.Application.Queries.WorkflowExecutions.GetExecutionBlocks;

/// <summary>
/// Returns every AI Query block in a persisted execution snapshot along with its
/// captured output and the resolved signature-field values used at execution time.
/// Deliberately omits the user/system prompts — those are Inia IP and stay server-side.
/// Consumed by the signature-test-replay playground's block/field picker.
/// </summary>
public record GetExecutionBlocksQuery(
    string OrganizationId,
    string UserId,
    string WorkflowExecutionId) : IRequest<ErrorOr<ExecutionBlocksDto>>;

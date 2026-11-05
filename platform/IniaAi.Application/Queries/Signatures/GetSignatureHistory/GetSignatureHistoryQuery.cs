using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Signatures;

namespace IniaAi.Application.Queries.Signatures.GetSignatureHistory;

/// <summary>
/// Returns one page of audit-log entries for a workflow's signature override value
/// transitions. Filtering is optional on placeholder, actor user, and a date range;
/// pagination is server-side via skip/take. Results are ordered newest-first.
/// </summary>
public record GetSignatureHistoryQuery(
    string OrganizationId,
    string UserId,
    string WorkflowTypeId,
    string? PlaceholderName = null,
    string? ActorUserId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int Skip = 0,
    int Take = 50) : IRequest<ErrorOr<SignatureHistoryPageDto>>;

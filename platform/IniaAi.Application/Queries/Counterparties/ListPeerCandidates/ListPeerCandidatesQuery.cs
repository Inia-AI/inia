using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Counterparty;

namespace IniaAi.Application.Queries.Counterparties.ListPeerCandidates;

/// <summary>
/// Asks which counterparties may be offered as peer companies, and whether each one's stored
/// financials can actually contribute a column.
/// </summary>
/// <param name="OrganizationId">The organization whose counterparties are offered.</param>
/// <param name="UserId">The user making the request.</param>
/// <param name="WorkflowTypeId">
/// The workflow the peers are being chosen for. Its Peer Financials block decides where in the
/// stored JSON the figures live, so eligibility is judged the way that workflow will read it —
/// and its counterparty type mappings decide which counterparties are peers at all: a
/// counterparty of a type the workflow does not accept is not offered. A workflow that maps no
/// types offers every type, as the counterparty picker does.
/// </param>
/// <param name="Search">Optional name fragment to narrow the list.</param>
/// <param name="Skip">How many candidates to skip, for paging.</param>
/// <param name="Top">
/// How many candidates to return. Each one costs a parse of its stored analysis, so this is
/// capped rather than optional — an organization's whole counterparty table is not a page.
/// </param>
public record ListPeerCandidatesQuery(
    string OrganizationId,
    string UserId,
    string? WorkflowTypeId,
    string? Search,
    int Skip = 0,
    int Top = ListPeerCandidatesQuery.DEFAULT_TOP) : IRequest<ErrorOr<List<PeerCandidateDto>>>
{
    public const int DEFAULT_TOP = 50;

    public const int MAX_TOP = 200;
}

using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.WorkflowReports;

namespace IniaAi.Application.Queries.WorkflowReports.ListReportDataOutputs;

/// <summary>
/// Handles the query to retrieve a report's <see cref="ReportDataOutputDto"/> list, ordered by name.
/// </summary>
public class ListReportDataOutputsQueryHandler
    : IRequestHandler<ListReportDataOutputsQuery, ErrorOr<List<ReportDataOutputDto>>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListReportDataOutputsQueryHandler"/> class.
    /// </summary>
    /// <param name="userDataUnitOfWork">The unit of work for user data repository access.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public ListReportDataOutputsQueryHandler(IUserDataUnitOfWork userDataUnitOfWork)
    {
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
    }

    /// <summary>
    /// Handles the incoming request to return a report's data outputs. An execution with no report,
    /// or one of another organization, returns an empty list, as the report's elements do.
    /// </summary>
    /// <param name="request">The request containing query options.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The report's data outputs, ordered by name.</returns>
    public async Task<ErrorOr<List<ReportDataOutputDto>>> Handle(ListReportDataOutputsQuery request, CancellationToken cancellationToken)
    {
        WorkflowReport? report = await _userDataUnitOfWork.Repository<WorkflowReport>()
            .GetFirstOrDefaultAsync(new WorkflowReportWithDataOutputsSpec(request.Id, request.OrganizationId), cancellationToken);

        IEnumerable<ReportDataOutput> outputs = report?.DataOutputs.OrderBy(output => output.Name, StringComparer.Ordinal)
            ?? Enumerable.Empty<ReportDataOutput>();

        List<ReportDataOutputDto> dataOutputs = [.. outputs.Select(ReportSectionDtoMapper.ToDto)];
        return dataOutputs;
    }
}

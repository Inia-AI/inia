using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.WorkflowReports;

namespace IniaAi.Application.Queries.WorkflowReports.ListReportSections;

/// <summary>
/// Handles the query to retrieve a report's <see cref="ReportSectionDto"/> tree.
/// </summary>
public class ListReportSectionsQueryHandler
    : IRequestHandler<ListReportSectionsQuery, ErrorOr<List<ReportSectionDto>>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    /// <summary>
    /// Initializes a new instance of the <see cref="ListReportSectionsQueryHandler"/> class.
    /// </summary>
    /// <param name="userDataUnitOfWork">The unit of work for user data repository access.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public ListReportSectionsQueryHandler(
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        _userDataUnitOfWork = userDataUnitOfWork ?? throw new ArgumentNullException(nameof(userDataUnitOfWork));
    }

    /// <summary>
    /// Handles the incoming request to return a report's section tree.
    /// </summary>
    /// <param name="request">The request containing query options.</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests.</param>
    /// <returns>The ordered sections of the report.</returns>
    public async Task<ErrorOr<List<ReportSectionDto>>> Handle(ListReportSectionsQuery request, CancellationToken cancellationToken)
    {
        var specification = new WorkflowReportWithDetailsSpec(request.Id, request.OrganizationId);

        WorkflowReport? report = await _userDataUnitOfWork.Repository<WorkflowReport>()
            .GetFirstOrDefaultAsync(specification, cancellationToken);

        IEnumerable<ReportSection> orderedSections = report?.Sections.OrderBy(s => s.Order)
            ?? Enumerable.Empty<ReportSection>();

        List<ReportSectionDto> sections = [.. orderedSections.Select(ReportSectionDtoMapper.ToDto)];
        return sections;
    }
}

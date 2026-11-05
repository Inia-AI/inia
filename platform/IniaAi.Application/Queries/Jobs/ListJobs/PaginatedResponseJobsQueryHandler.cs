using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.OData;
using IniaAi.Application.Mappers;
using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Jobs;

namespace IniaAi.Application.Queries.Jobs.ListJobs;

/// <summary>
/// Handles the query to retrieve a paginated and OData-compliant list of <see cref="JobDto"/>.
/// The jobs live in memory in the jobs service, so the query options run over the mapped jobs.
/// </summary>
public class PaginatedResponseJobsQueryHandler : IRequestHandler<PaginatedResponseJobsQuery, ErrorOr<ODataPaginatedResponse<JobDto>>>
{
    private readonly IJobsService _jobService;

    /// <summary>
    /// Initializes a new instance of the <see cref="PaginatedResponseJobsQueryHandler"/> class.
    /// </summary>
    /// <param name="jobService">Service for retrieving background jobs.</param>
    public PaginatedResponseJobsQueryHandler(IJobsService jobService)
    {
        _jobService = jobService ?? throw new ArgumentNullException(nameof(jobService));
    }

    /// <summary>
    /// Handles the query request to return paginated jobs.
    /// </summary>
    /// <param name="request">The request containing OData query options.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A paginated OData response containing job DTOs.</returns>
    public async Task<ErrorOr<ODataPaginatedResponse<JobDto>>> Handle(PaginatedResponseJobsQuery request, CancellationToken cancellationToken)
    {
        IQueryable<JobDto> jobs = _jobService.GetOrganizationJobs(request.OrganizationId)
            .Where(job => job.UserId == request.UserId)
            .Select(BackgroundJobMapper.MapToResponse)
            .AsQueryable();

        return await jobs.ToODataPageAsync(request.QueryOptions, cancellationToken);
    }
}

using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Mappers;
using IniaAi.Common.Text;
using IniaAi.Services.Models.BackgroundJobs;
using IniaAi.WebApi.Contracts.Responses.Jobs;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queries.Jobs.GetJob;

/// <summary>
/// Handles <see cref="GetJobQuery"/> to retrieve a job by ID.
/// </summary>
public sealed class GetJobQueryHandler : IRequestHandler<GetJobQuery, ErrorOr<JobDto>>
{
    private readonly ILogger<GetJobQueryHandler> _logger;
    private readonly IJobsService _jobService;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetJobQueryHandler"/> class.
    /// </summary>
    /// <param name="logger">Logger for operational diagnostics.</param>
    /// <param name="jobService">The job service used to retrieve job data.</param>
    public GetJobQueryHandler(ILogger<GetJobQueryHandler> logger, IJobsService jobService)
    {
        _logger = logger;
        _jobService = jobService;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<JobDto>> Handle(GetJobQuery request, CancellationToken cancellationToken)
    {
        BackgroundJob? job = _jobService.GetJob(request.Id);

        if (job is null || job.OrganizationId != request.OrganizationId)
        {
            _logger.LogWarning("Job with ID {JobId} was not found in the caller's organization.", LogSanitizer.ForLog(request.Id));
            return Error.NotFound(description: "Job not found.");
        }

        if (job.UserId != request.UserId)
        {
            _logger.LogWarning("User {UserId} is not authorized to access job {JobId}.", job.UserId, LogSanitizer.ForLog(request.Id));
            return Error.Forbidden(description: "You are not authorized to access this job.");
        }

        JobDto dto = BackgroundJobMapper.MapToResponse(job);
        return await Task.FromResult(dto);
    }
}

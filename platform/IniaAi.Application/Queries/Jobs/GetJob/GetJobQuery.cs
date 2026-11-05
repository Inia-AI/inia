using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.WebApi.Contracts.Responses.Jobs;

namespace IniaAi.Application.Queries.Jobs.GetJob;

/// <summary>
/// Query to retrieve a job by its unique identifier.
/// </summary>
/// <param name="Id">The unique identifier of the job.</param>
public sealed record GetJobQuery(
    string Id,
    string? OrganizationId,
    string? UserId) : IRequest<ErrorOr<JobDto>>;

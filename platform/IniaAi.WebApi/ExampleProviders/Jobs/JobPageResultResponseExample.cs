using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Jobs;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.Jobs;

/// <summary>
/// Provides an example instance of <see cref="ODataPaginatedResponse{JobDto}"/> for Swagger documentation.
/// </summary>
public class JobPageResultResponseExample : IExamplesProvider<ODataPaginatedResponse<JobDto>>
{
    /// <summary>
    /// Returns an example of <see cref="ODataPaginatedResponse{JobDto}"/> populated with sample data.
    /// </summary>
    /// <returns>An instance of <see cref="ODataPaginatedResponse{JobDto}"/> with sample values.</returns>
    public ODataPaginatedResponse<JobDto> GetExamples()
    {
        var jobs = new List<JobDto>
        {
            new()
            {
                Id = "job-1234",
                Type = Contracts.Enums.BackgroundJobType.WorkflowExecution,
                State = "Completed",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
                CompletedAt = DateTime.UtcNow.AddDays(-1),
                UserId = "user-5678"
            },
            new()
            {
                Id = "job-5678",
                Type = Contracts.Enums.BackgroundJobType.WorkflowExecution,
                State = "Running",
                CreatedAt = DateTime.UtcNow.AddHours(-5),
                CompletedAt = null,
                UserId = "user-5678"
            }
        };

        return new ODataPaginatedResponse<JobDto>(
            jobs,
            totalCount: jobs.Count,
            skip: 0,
            top: 25
        );
    }
}

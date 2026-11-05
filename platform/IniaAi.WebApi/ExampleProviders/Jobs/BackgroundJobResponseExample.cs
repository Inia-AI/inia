using IniaAi.Services.Models.BackgroundJobs.Enums;
using IniaAi.Services.Models.Jobs.Enums;
using IniaAi.WebApi.Contracts.Responses.Jobs;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.Jobs;

/// <summary>
/// Provides an example response for a background job in Swagger documentation.
/// </summary>
public class BackgroundJobResponseExample : IExamplesProvider<JobDto>
{
    /// <summary>
    /// Returns an example instance of <see cref="JobDto"/> representing a completed workflow execution job.
    /// </summary>
    /// <returns>An example of <see cref="JobDto"/>.</returns>
    public JobDto GetExamples()
    {
        return new JobDto
        {
            Id = "b9c3e4b2-3c0d-4d59-9e91-bb7d2d1cfea1",
            State = JobState.Done.ToString(),
            Type = (Contracts.Enums.BackgroundJobType)BackgroundJobType.WorkflowExecution,
            Error = null,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            CompletedAt = DateTime.UtcNow,
            Progress = 100,
            Description = "Workflow execution job."
        };
    }
}

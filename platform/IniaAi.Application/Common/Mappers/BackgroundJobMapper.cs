using IniaAi.Services.Models.BackgroundJobs;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.Jobs;

namespace IniaAi.Application.Mappers;

/// <summary>
/// Provides mapping functions for background job domain models to DTOs.
/// </summary>
public static class BackgroundJobMapper
{
    /// <summary>
    /// Creates a <see cref="JobDto"/> from the specified <see cref="BackgroundJob"/> instance.
    /// </summary>
    /// <param name="job">The background job to convert. Must not be <c>null</c>.</param>
    /// <returns>A <see cref="JobDto"/> that represents the background job.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="job"/> is <c>null</c>.</exception>
    public static JobDto MapToResponse(BackgroundJob job)
    {
        ArgumentNullException.ThrowIfNull(job);

        return new JobDto
        {
            Id = job.Id,
            State = job.State.ToString(),
            Type = (BackgroundJobType)job.Type,
            Error = job.Error,
            CreatedAt = job.CreatedAt,
            CompletedAt = job.CompletedAt,
            Progress = job.Progress,
            Description = job.Description,
            UserId = job.UserId,
        };
    }
}

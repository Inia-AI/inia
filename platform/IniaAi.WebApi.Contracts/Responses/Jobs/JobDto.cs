using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.Jobs;

/// <summary>
/// Represents the response model containing details about a background job.
/// </summary>
public class JobDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the background job.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current state of the job.
    ///</summary>
    public string State { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the type of the job.
    /// </summary>
    public BackgroundJobType Type { get; set; }

    /// <summary>
    /// Gets or sets the error message if the job encountered an error; otherwise, null.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the job was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the job was completed; null if not completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the progress of the job as a percentage.
    /// </summary>
    public double Progress { get; set; }

    /// <summary>
    /// Gets or sets the description of the job.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user identifier associated with the job.
    /// </summary>
    public string? UserId { get; set; } = string.Empty;
}

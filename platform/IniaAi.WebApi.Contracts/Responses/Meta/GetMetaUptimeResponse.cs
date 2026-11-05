namespace IniaAi.WebApi.Contracts.Responses.Meta;

/// <summary>
/// Represents the response containing the application's uptime information.
/// </summary>
public class GetMetaUptimeResponse
{
    /// <summary>
    /// Gets or sets the UTC date and time when the application started.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the duration for which the application has been running since startup.
    /// </summary>
    public TimeSpan Uptime { get; set; }
}

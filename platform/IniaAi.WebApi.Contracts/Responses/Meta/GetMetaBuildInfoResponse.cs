namespace IniaAi.WebApi.Contracts.Responses.Meta;

/// <summary>
/// Represents metadata information about the build, including commit SHA, build date, and branch name.
/// </summary>
public class GetMetaBuildInfoResponse
{
    /// <summary>
    /// Gets or sets the SHA hash of the commit associated with the build.
    /// </summary>
    public string CommitSha { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC date and time when the build was created.
    /// </summary>
    public DateTime BuildDateUtc { get; set; }

    /// <summary>
    /// Gets or sets the name of the branch from which the build was generated.
    /// </summary>
    public string Branch { get; set; } = string.Empty;
}


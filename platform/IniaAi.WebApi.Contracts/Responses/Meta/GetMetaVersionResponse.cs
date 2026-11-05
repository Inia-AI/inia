namespace IniaAi.WebApi.Contracts.Responses.Meta;

/// <summary>
/// Represents the response containing version information for metadata.
/// </summary>
public class GetMetaVersionResponse
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GetMetaVersionResponse"/> class with the specified version and version information.
    /// </summary>
    /// <param name="version">The version identifier.</param>
    /// <param name="versionInformation">Additional information about the version.</param>
    public GetMetaVersionResponse(string version, string versionInformation)
    {
        Version = version;
        VersionInformation = versionInformation;
    }

    /// <summary>
    /// Gets or sets the version identifier.
    /// </summary>
    public string Version { get; set; }

    /// <summary>
    /// Gets or sets additional information about the version.
    /// </summary>
    public string VersionInformation { get; set; }
}


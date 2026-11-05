namespace IniaAi.Configuration.Platform;

public sealed class GitHubSettings
{
    public bool Enabled { get; set; } = false;
    public string? Owner { get; set; }
    public string? Repo { get; set; }
    public string DefaultBranch { get; set; } = "main";
    public string? Pat { get; set; }

    /// <summary>
    /// The committer of every mirrored version.
    /// </summary>
    public string CommitterName { get; set; } = "Workflow versioning";

    public string CommitterEmail { get; set; } = "workflow-versioning@example.com";

    /// <summary>
    /// The author of a version saved through the template editing API.
    /// </summary>
    public string ApiAuthorName { get; set; } = "Workflow API";

    public string ApiAuthorEmail { get; set; } = "workflow-api@example.com";
}

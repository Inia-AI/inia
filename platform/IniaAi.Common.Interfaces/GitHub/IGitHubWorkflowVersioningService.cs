namespace IniaAi.Common.Interfaces.GitHub;

public interface IGitHubWorkflowVersioningService
{
    Task<GitHubCommitResult> CommitWorkflowAsync(
        string workflowTypeId,
        string area,
        string function,
        string content,
        string commitMessage,
        string authorName,
        string authorEmail,
        CancellationToken cancellationToken = default);
}

public sealed class GitHubCommitResult
{
    public string Sha { get; init; } = string.Empty;
    public string HtmlUrl { get; init; } = string.Empty;
    public bool Created { get; init; }
}

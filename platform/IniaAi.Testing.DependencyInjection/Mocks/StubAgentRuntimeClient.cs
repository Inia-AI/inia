using ErrorOr;
using IniaAi.Common.Interfaces.Services.AgentRuntime;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// Keeps BlockExecutionContext resolvable in test hosts without a sidecar. Every call answers
/// with a distinct unexpected error, so a test that unintentionally reaches the agent runtime
/// fails loudly instead of hanging on HTTP. Tests exercising agent paths program the per-verb
/// hooks (and can record what was sent) instead of standing up a second client.
/// </summary>
public class StubAgentRuntimeClient : IAgentRuntimeClient
{
    private const string ERROR_CODE = "AgentRun.StubClient";

    public List<AgentRunStartRequest> StartRequests { get; } = [];
    public List<string> CompletedExecutionIds { get; } = [];
    public List<string> CancelledExecutionIds { get; } = [];
    public List<string> FetchedFilePaths { get; } = [];
    public List<string> CheckpointFetchedExecutionIds { get; } = [];

    public Func<AgentRunStartRequest, ErrorOr<AgentRunMonitor>>? OnStartRun { get; set; }
    public Func<string, ErrorOr<AgentRunMonitor>>? OnGetRun { get; set; }
    public Func<string, ErrorOr<AgentRunMonitor>>? OnCancelRun { get; set; }
    public Func<string, ErrorOr<AgentRunMonitor>>? OnCompleteRun { get; set; }
    public Func<string, ErrorOr<IReadOnlyList<AgentRunFileEntry>>>? OnListRunFiles { get; set; }
    public Func<string, string, ErrorOr<byte[]>>? OnGetRunFile { get; set; }
    public Func<string, ErrorOr<AgentRunCheckpoint>>? OnGetRunCheckpoint { get; set; }

    /// <summary>
    /// Token-aware variant of <see cref="OnGetRunFile"/>, for tests that assert how the
    /// executor's cancellation reaches a file fetch. Wins over the synchronous hook when both
    /// are set.
    /// </summary>
    public Func<string, string, CancellationToken, Task<ErrorOr<byte[]>>>? OnGetRunFileAsync { get; set; }

    public Task<ErrorOr<AgentRunMonitor>> StartRunAsync(
        string executionId,
        string prompt,
        string runToken,
        CancellationToken cancellationToken = default)
    {
        return StartRunAsync(new AgentRunStartRequest(executionId, prompt, runToken), cancellationToken);
    }

    public Task<ErrorOr<AgentRunMonitor>> StartRunAsync(
        AgentRunStartRequest request,
        CancellationToken cancellationToken = default)
    {
        StartRequests.Add(request);

        return OnStartRun is null ? Stub<AgentRunMonitor>() : Task.FromResult(OnStartRun(request));
    }

    public Task<ErrorOr<AgentRunMonitor>> GetRunAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        return OnGetRun is null ? Stub<AgentRunMonitor>() : Task.FromResult(OnGetRun(executionId));
    }

    public Task<ErrorOr<AgentRunMonitor>> CancelRunAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        CancelledExecutionIds.Add(executionId);

        return OnCancelRun is null ? Stub<AgentRunMonitor>() : Task.FromResult(OnCancelRun(executionId));
    }

    public Task<ErrorOr<AgentRunMonitor>> SubmitInputAsync(
        string executionId,
        string resumeToken,
        string questionId,
        string answer,
        bool allowFurtherPause = true,
        CancellationToken cancellationToken = default)
    {
        return Stub<AgentRunMonitor>();
    }

    public Task<ErrorOr<IReadOnlyList<AgentRunFileEntry>>> ListRunFilesAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        return OnListRunFiles is null
            ? Stub<IReadOnlyList<AgentRunFileEntry>>()
            : Task.FromResult(OnListRunFiles(executionId));
    }

    public Task<ErrorOr<byte[]>> GetRunFileAsync(
        string executionId,
        string path,
        CancellationToken cancellationToken = default)
    {
        FetchedFilePaths.Add(path);

        if (OnGetRunFileAsync is not null)
        {
            return OnGetRunFileAsync(executionId, path, cancellationToken);
        }

        return OnGetRunFile is null ? Stub<byte[]>() : Task.FromResult(OnGetRunFile(executionId, path));
    }

    public Task<ErrorOr<AgentRunCheckpoint>> GetRunCheckpointAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        CheckpointFetchedExecutionIds.Add(executionId);

        return OnGetRunCheckpoint is null
            ? Stub<AgentRunCheckpoint>()
            : Task.FromResult(OnGetRunCheckpoint(executionId));
    }

    public Task<ErrorOr<AgentRunMonitor>> CompleteRunAsync(
        string executionId,
        CancellationToken cancellationToken = default)
    {
        CompletedExecutionIds.Add(executionId);

        return OnCompleteRun is null ? Stub<AgentRunMonitor>() : Task.FromResult(OnCompleteRun(executionId));
    }

    public Func<ErrorOr<AgentRunLimits>>? OnGetLimits { get; set; }

    public Task<ErrorOr<AgentRunLimits>> GetLimitsAsync(CancellationToken cancellationToken = default)
    {
        return OnGetLimits is null ? Stub<AgentRunLimits>() : Task.FromResult(OnGetLimits());
    }

    public Func<ErrorOr<AgentRuntimeEnvironment>>? OnGetEnvironment { get; set; }

    public int EnvironmentFetchCount { get; private set; }

    public Task<ErrorOr<AgentRuntimeEnvironment>> GetEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        EnvironmentFetchCount++;

        return OnGetEnvironment is null ? Stub<AgentRuntimeEnvironment>() : Task.FromResult(OnGetEnvironment());
    }

    private static Task<ErrorOr<T>> Stub<T>()
    {
        return Task.FromResult<ErrorOr<T>>(Error.Unexpected(
            ERROR_CODE,
            "The test host's agent runtime client is a stub — register a real or test double client to exercise agent runs"));
    }
}

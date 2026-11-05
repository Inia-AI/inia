using IniaAi.Services.Models.Jobs;

namespace IniaAi.Services.Jobs;

public interface IJobExecutor
{
    /// <summary>
    /// Starts the job on a background task and returns immediately. Once the job is accepted,
    /// <paramref name="onFinish"/> is invoked exactly once on every outcome — success, failure,
    /// cancellation, or fencing — so callers may rely on it for cleanup. A synchronous throw
    /// means the job was never started and <paramref name="onFinish"/> will not be called.
    /// </summary>
    void Execute(Job job, Action<Job> onFinish, CancellationToken cancellationToken);
}

using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Services.Models.Jobs;
using System.Threading.Channels;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// Stops the jobs a test started, for its teardown. A job runs on a thread of its own and can
/// outlast what the test waited for: a conversation turn's job reads the database after the turn
/// has settled, to start the next waiting turn. A teardown that disposes the database first lets
/// that thread open the database file again.
/// </summary>
public static class JobsServiceTeardown
{
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Cancels every unfinished job the service holds, including one a cancelled job starts as it
    /// ends, and completes when none is left running. A delegated job, such as a conversation turn,
    /// counts as running until it leaves the service, which is its last step. A workflow job stays
    /// listed after it ends, so it counts once it has finished; the service gives no later signal.
    /// </summary>
    /// <param name="timeout">How long the jobs may take to stop; 30 seconds when not given.</param>
    public static async Task StopEveryJobAsync(this IJobsService jobs, TimeSpan? timeout = null)
    {
        TimeSpan limit = timeout ?? _defaultTimeout;
        Channel<bool> changes = Channel.CreateUnbounded<bool>();
        void OnCollectionChanged(object? sender, EventArgs args) => _ = changes.Writer.TryWrite(true);
        void OnStateChanged(object? sender, JobStateChangeEventArgs args) => _ = changes.Writer.TryWrite(true);

        jobs.JobCollectionChanged += OnCollectionChanged;
        jobs.JobStateChanged += OnStateChanged;
        try
        {
            using var deadline = new CancellationTokenSource(limit);
            while (true)
            {
                List<Job> running = await RunningAsync(jobs);
                if (running.Count == 0)
                {
                    return;
                }

                foreach (Job job in running)
                {
                    jobs.CancelJob(job.Id);
                }

                try
                {
                    _ = await changes.Reader.ReadAsync(deadline.Token);
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested)
                {
                    List<Job> stillRunning = await RunningAsync(jobs);
                    throw new TimeoutException(
                        $"{stillRunning.Count} job(s) still ran {limit.TotalSeconds} s after the teardown cancelled them: {string.Join(", ", stillRunning.Select(static job => $"{job.Id} {job.Description}".Trim()))}.");
                }
            }
        }
        finally
        {
            jobs.JobCollectionChanged -= OnCollectionChanged;
            jobs.JobStateChanged -= OnStateChanged;
        }
    }

    private static async Task<List<Job>> RunningAsync(IJobsService jobs)
    {
        return [.. (await jobs.GetJobsAsync()).Where(static job => job is DelegatedJob || !job.IsFinished)];
    }
}

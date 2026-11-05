using System.Collections.Concurrent;
using System.Reflection;

namespace IniaAi.Domain.Reruns;

/// <summary>
/// Whether the code running now belongs to a rerun (<c>Job.IsRerun</c>). The job
/// executor opens the scope for the whole job, and the save step of every data context drops the
/// rerun's changes to <see cref="LiveDataAttribute"/> entities, so a rerun is executed and scored
/// but leaves live data as it was. The flag flows with the job's async calls, so parallel blocks
/// and fresh contexts inside the job see it too.
/// </summary>
public static class RerunWrites
{
    private static readonly AsyncLocal<bool> _suppressed = new();
    private static readonly ConcurrentDictionary<Type, bool> _liveDataTypes = new();

    public static bool AreSuppressed => _suppressed.Value;

    public static IDisposable Suppress()
    {
        return Scope(isRerun: true);
    }

    /// <summary>
    /// Sets the flag both ways for the code that runs inside the scope, so a report's job never
    /// inherits a rerun's flag from the context that started it.
    /// </summary>
    public static IDisposable Scope(bool isRerun)
    {
        bool previous = _suppressed.Value;
        _suppressed.Value = isRerun;

        return new Restore(previous);
    }

    public static bool IsLiveData(Type entityType)
    {
        return _liveDataTypes.GetOrAdd(
            entityType,
            static type => type.GetCustomAttribute<LiveDataAttribute>(inherit: true) is not null);
    }

    private sealed class Restore(bool previous) : IDisposable
    {
        public void Dispose()
        {
            _suppressed.Value = previous;
        }
    }
}

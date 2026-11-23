using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Xunit.v3;

namespace IniaAi.Testing.XunitAttributes;

/// <summary>
/// Fails a test on CI that runs longer than <see cref="Ceiling"/> without a <see cref="SlowTestAttribute"/>.
/// The ceiling sits far above a normal test because a two-CPU runner makes single test times noisy;
/// it catches a test that waits on a clock or a network, not one that is merely slow. A test that
/// starts before any test of its process has finished also pays the start of the process, so it
/// gets <see cref="StartupAllowance"/> on top.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class TestTimeCeilingAttribute : BeforeAfterTestAttribute
{
    public static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

    // The first tests of a process share its start: the JIT, the first host, the first database
    // connection. On a slow runner that cost alone once took a 1-second test past 30 seconds.
    public static readonly TimeSpan StartupAllowance = TimeSpan.FromSeconds(30);

    private static readonly ConcurrentDictionary<string, (long StartedAt, bool StartedCold)> _started = new();
    private static int _finishedTests;

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        _started[test.UniqueID] = (Stopwatch.GetTimestamp(), Volatile.Read(ref _finishedTests) == 0);
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!_started.TryRemove(test.UniqueID, out (long StartedAt, bool StartedCold) started))
        {
            return;
        }

        _ = Interlocked.Increment(ref _finishedTests);
        string? violation = Violation(Stopwatch.GetElapsedTime(started.StartedAt), methodUnderTest, Utils.IsRunningOnCI(), started.StartedCold);
        if (violation is not null)
        {
            throw new TimeoutException(violation);
        }
    }

    internal static bool IsTiming(string testUniqueId)
    {
        return _started.ContainsKey(testUniqueId);
    }

    internal static string? Violation(TimeSpan elapsed, MethodInfo methodUnderTest, bool onCi, bool startedCold = false)
    {
        TimeSpan limit = startedCold ? Ceiling + StartupAllowance : Ceiling;
        if (!onCi || elapsed <= limit || IsMarkedSlow(methodUnderTest))
        {
            return null;
        }

        string allowance = startedCold ? $" and the {StartupAllowance.TotalSeconds:F0} s it adds for the start of the test process" : string.Empty;
        return $"{methodUnderTest.DeclaringType?.Name}.{methodUnderTest.Name} took {elapsed.TotalSeconds:F1} s, " +
            $"over the {Ceiling.TotalSeconds:F0} s ceiling{allowance}. Remove the wait, or mark the test " +
            $"[SlowTest(\"what the time buys\")].";
    }

    private static bool IsMarkedSlow(MethodInfo methodUnderTest)
    {
        return methodUnderTest.IsDefined(typeof(SlowTestAttribute), inherit: true)
            || methodUnderTest.IsDefined(typeof(NightlyFactAttribute), inherit: true)
            || methodUnderTest.DeclaringType?.IsDefined(typeof(SlowTestAttribute), inherit: true) == true;
    }
}

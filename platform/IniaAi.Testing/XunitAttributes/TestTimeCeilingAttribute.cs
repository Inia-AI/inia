using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Xunit.v3;

namespace IniaAi.Testing.XunitAttributes;

/// <summary>
/// Fails a test on CI that runs longer than <see cref="Ceiling"/> without a <see cref="SlowTestAttribute"/>.
/// The ceiling sits far above a normal test because a two-CPU runner makes single test times noisy;
/// it catches a test that waits on a clock or a network, not one that is merely slow.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class TestTimeCeilingAttribute : BeforeAfterTestAttribute
{
    public static readonly TimeSpan Ceiling = TimeSpan.FromSeconds(30);

    private static readonly ConcurrentDictionary<string, long> _startedAt = new();

    public override void Before(MethodInfo methodUnderTest, IXunitTest test)
    {
        _startedAt[test.UniqueID] = Stopwatch.GetTimestamp();
    }

    public override void After(MethodInfo methodUnderTest, IXunitTest test)
    {
        if (!_startedAt.TryRemove(test.UniqueID, out long startedAt))
        {
            return;
        }

        string? violation = Violation(Stopwatch.GetElapsedTime(startedAt), methodUnderTest, Utils.IsRunningOnCI());
        if (violation is not null)
        {
            throw new TimeoutException(violation);
        }
    }

    internal static bool IsTiming(string testUniqueId)
    {
        return _startedAt.ContainsKey(testUniqueId);
    }

    internal static string? Violation(TimeSpan elapsed, MethodInfo methodUnderTest, bool onCi)
    {
        if (!onCi || elapsed <= Ceiling || IsMarkedSlow(methodUnderTest))
        {
            return null;
        }

        return $"{methodUnderTest.DeclaringType?.Name}.{methodUnderTest.Name} took {elapsed.TotalSeconds:F1} s, " +
            $"over the {Ceiling.TotalSeconds:F0} s ceiling. Remove the wait, or mark the test " +
            $"[SlowTest(\"what the time buys\")].";
    }

    private static bool IsMarkedSlow(MethodInfo methodUnderTest)
    {
        return methodUnderTest.IsDefined(typeof(SlowTestAttribute), inherit: true)
            || methodUnderTest.IsDefined(typeof(NightlyFactAttribute), inherit: true)
            || methodUnderTest.DeclaringType?.IsDefined(typeof(SlowTestAttribute), inherit: true) == true;
    }
}

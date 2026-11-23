using System.Reflection;
using IniaAi.Testing.XunitAttributes;

namespace IniaAi.Common.Tests;

public class TestTimeCeilingTests
{
    private static readonly TimeSpan _overTheCeiling = TestTimeCeilingAttribute.Ceiling + TimeSpan.FromSeconds(1);

    [Fact]
    public void EveryTestOfThisAssembly_IsTimed()
    {
        Assert.True(TestTimeCeilingAttribute.IsTiming(TestContext.Current.Test!.UniqueID));
    }

    [Fact]
    public void OnCi_AnUnmarkedTestOverTheCeiling_FailsAndSaysHowToFixIt()
    {
        string? violation = TestTimeCeilingAttribute.Violation(_overTheCeiling, Method(typeof(Samples), nameof(Samples.Unmarked)), onCi: true);

        Assert.NotNull(violation);
        Assert.Contains(nameof(Samples.Unmarked), violation);
        Assert.Contains("[SlowTest(", violation);
    }

    [Fact]
    public void OnCi_ATestMarkedSlow_MayTakeLonger()
    {
        Assert.Null(TestTimeCeilingAttribute.Violation(_overTheCeiling, Method(typeof(Samples), nameof(Samples.Marked)), onCi: true));
    }

    [Fact]
    public void OnCi_ATestInAClassMarkedSlow_MayTakeLonger()
    {
        Assert.Null(TestTimeCeilingAttribute.Violation(_overTheCeiling, Method(typeof(SlowSamples), nameof(SlowSamples.Any)), onCi: true));
    }

    [Fact]
    public void OnCi_AnUnmarkedTestUnderTheCeiling_Passes()
    {
        Assert.Null(TestTimeCeilingAttribute.Violation(TestTimeCeilingAttribute.Ceiling, Method(typeof(Samples), nameof(Samples.Unmarked)), onCi: true));
    }

    [Fact]
    public void OnCi_ATestThatStartsWithTheProcess_AlsoGetsTheProcessStart()
    {
        TimeSpan pastTheCeilingInsideTheAllowance = TestTimeCeilingAttribute.Ceiling + (TestTimeCeilingAttribute.StartupAllowance / 2);

        Assert.Null(TestTimeCeilingAttribute.Violation(pastTheCeilingInsideTheAllowance, Method(typeof(Samples), nameof(Samples.Unmarked)), onCi: true, startedCold: true));
        Assert.NotNull(TestTimeCeilingAttribute.Violation(pastTheCeilingInsideTheAllowance, Method(typeof(Samples), nameof(Samples.Unmarked)), onCi: true, startedCold: false));
    }

    [Fact]
    public void OnCi_ATestThatStartsWithTheProcess_StillFailsPastTheAllowance_AndSaysSo()
    {
        TimeSpan pastTheAllowance = TestTimeCeilingAttribute.Ceiling + TestTimeCeilingAttribute.StartupAllowance + TimeSpan.FromSeconds(1);

        string? violation = TestTimeCeilingAttribute.Violation(pastTheAllowance, Method(typeof(Samples), nameof(Samples.Unmarked)), onCi: true, startedCold: true);

        Assert.NotNull(violation);
        Assert.Contains("start of the test process", violation);
        Assert.Contains("[SlowTest(", violation);
    }

    [Fact]
    public void OffCi_NoTestFailsForItsTime()
    {
        Assert.Null(TestTimeCeilingAttribute.Violation(_overTheCeiling, Method(typeof(Samples), nameof(Samples.Unmarked)), onCi: false));
    }

    private static MethodInfo Method(Type type, string name)
    {
        return type.GetMethod(name)!;
    }

    private static class Samples
    {
        public static void Unmarked()
        {
        }

        [SlowTest("renders a large page")]
        public static void Marked()
        {
        }
    }

    [SlowTest("every test drives a real browser")]
    private static class SlowSamples
    {
        public static void Any()
        {
        }
    }
}

using IniaAi.Testing.XunitAttributes;

namespace IniaAi.Common.Tests;

public class NightlyOnlyTests
{
    private const string REASON = "anonymises a whole table page";

    [Fact]
    public void OnAPullRequestRun_ANightlyTestSkipsAndSaysWhereItRuns()
    {
        string? skip = NightlyOnly.SkipReason(REASON, onCi: true, isNightly: false);

        Assert.NotNull(skip);
        Assert.Contains("nightly", skip, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(REASON, skip);
    }

    [Fact]
    public void InTheNightlyRun_ANightlyTestRuns()
    {
        Assert.Null(NightlyOnly.SkipReason(REASON, onCi: true, isNightly: true));
    }

    [Fact]
    public void OffCi_ANightlyTestRuns()
    {
        Assert.Null(NightlyOnly.SkipReason(REASON, onCi: false, isNightly: false));
    }

    [Fact]
    public void ANightlyTest_MayRunPastTheCeiling()
    {
        TimeSpan overTheCeiling = TestTimeCeilingAttribute.Ceiling + TimeSpan.FromSeconds(1);

        Assert.Null(TestTimeCeilingAttribute.Violation(overTheCeiling, typeof(Samples).GetMethod(nameof(Samples.Nightly))!, onCi: true));
    }

#pragma warning disable xUnit1000 // Carries the attribute for reflection only; it is not a test class.
    private static class Samples
    {
        [NightlyFact(REASON)]
        public static void Nightly()
        {
        }
    }
#pragma warning restore xUnit1000
}

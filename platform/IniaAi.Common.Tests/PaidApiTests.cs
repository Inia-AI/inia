using IniaAi.Testing.XunitAttributes;

namespace IniaAi.Common.Tests;

public class PaidApiTests
{
    private const string REASON = "calls a model";

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void WithoutTheOptIn_APaidTestSkips_OnCiAndOffIt(bool onCi, bool alsoInCi)
    {
        string? skip = PaidApi.SkipReason(REASON, alsoInCi, onCi, optedIn: false);

        Assert.NotNull(skip);
        Assert.Contains(PaidApi.OPT_IN_VARIABLE, skip);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void WithTheOptIn_APaidTestRunsOffCi(bool alsoInCi)
    {
        Assert.Null(PaidApi.SkipReason(REASON, alsoInCi, onCi: false, optedIn: true));
    }

    [Fact]
    public void WithTheOptIn_OnCi_OnlyATestMarkedForCiRuns()
    {
        Assert.Null(PaidApi.SkipReason(REASON, alsoInCi: true, onCi: true, optedIn: true));
        Assert.StartsWith("Ignored in CI", PaidApi.SkipReason(REASON, alsoInCi: false, onCi: true, optedIn: true));
    }
}

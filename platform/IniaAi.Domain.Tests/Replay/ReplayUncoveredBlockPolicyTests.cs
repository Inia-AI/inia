using IniaAi.Domain.Replay;

namespace IniaAi.Domain.Tests.Replay;

public class ReplayUncoveredBlockPolicyTests
{
    [Fact]
    public void Default_IsTheDormantRegressionSemantics()
    {
        Assert.Equal(ReplayUncoveredBlockPolicy.SkipAsInconclusive, default(ReplayUncoveredBlockPolicy));
    }
}

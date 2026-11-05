using IniaAi.Application.Common.DailyAllowances;
using IniaAi.Domain.Entities.DailyAllowances;

namespace IniaAi.Application.UnitTests.Common.DailyAllowances;

/// <summary>The refusal counts the user's requests in the singular for a limit of one.</summary>
public class DailyAllowanceKindsTests
{
    [Theory]
    [InlineData(DailyAllowanceKind.Explore, 1, "You have used your 1 daily explore question. You can ask more from midnight UTC.")]
    [InlineData(DailyAllowanceKind.Explore, 7, "You have used your 7 daily explore questions. You can ask more from midnight UTC.")]
    [InlineData(DailyAllowanceKind.Refine, 1, "You have used your 1 daily refine request. You can refine again from midnight UTC.")]
    [InlineData(DailyAllowanceKind.Refine, 5, "You have used your 5 daily refine requests. You can refine again from midnight UTC.")]
    [InlineData(DailyAllowanceKind.HelpRequest, 1, "You have filed your 1 daily help request. You can file another from midnight UTC, or reply to the request you filed.")]
    [InlineData(DailyAllowanceKind.HelpRequestReply, 1, "You have sent your 1 daily reply to support. You can reply again from midnight UTC.")]
    [InlineData(DailyAllowanceKind.HelpRequestReply, 30, "You have sent your 30 daily replies to support. You can reply again from midnight UTC.")]
    [InlineData(DailyAllowanceKind.HelpRequest, 10, "You have filed your 10 daily help requests. You can file more from midnight UTC, or reply to a request you filed.")]
    public void TheRefusal_MatchesTheLimitInNumber(DailyAllowanceKind kind, int limit, string message)
    {
        Assert.Equal(message, DailyAllowanceKinds.All[kind].ExhaustedMessageFor(limit));
    }

    [Fact]
    public void EveryKind_HasADefinition()
    {
        Assert.All(Enum.GetValues<DailyAllowanceKind>(), kind => Assert.True(DailyAllowanceKinds.All.ContainsKey(kind)));
    }
}

using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Tests.Data.Workflows.Properties;

public class VerificationMemberTests
{
    private static VerificationMember ScratchMember(string tag)
    {
        return new VerificationMember { MemberTag = tag, RedoMode = VerificationRedoMode.Scratch };
    }

    [Fact]
    public void PointAt_ADifferentTag_ResetsTheModeToRevise()
    {
        VerificationMember member = ScratchMember("BalanceSheet");

        member.PointAt("CashFlow");

        Assert.Equal("CashFlow", member.MemberTag);
        Assert.Equal(VerificationRedoMode.Revise, member.RedoMode);
    }

    [Fact]
    public void PointAt_TheSameTag_KeepsTheChosenMode()
    {
        VerificationMember member = ScratchMember("BalanceSheet");

        member.PointAt("BalanceSheet");

        Assert.Equal("BalanceSheet", member.MemberTag);
        Assert.Equal(VerificationRedoMode.Scratch, member.RedoMode);
    }

    [Fact]
    public void PointAt_Null_ClearsTheTagAndResetsTheMode()
    {
        VerificationMember member = ScratchMember("BalanceSheet");

        member.PointAt(null);

        Assert.Equal(string.Empty, member.MemberTag);
        Assert.Equal(VerificationRedoMode.Revise, member.RedoMode);
    }
}

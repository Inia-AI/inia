using IniaAi.Domain.Constants;

namespace IniaAi.Domain.Tests.Constants;

public class StopMarkerTests
{
    [Theory]
    [InlineData("AF11")]
    [InlineData("af11")]
    [InlineData("<stop>AF11</stop>")]
    [InlineData("Has parent: AF11")]
    [InlineData("before AF11 after")]
    [InlineData("\"AF11\"")]
    [InlineData("line one\nAF11\nline three")]
    [InlineData("(AF11)")]
    [InlineData("[AF11]")]
    [InlineData("AF11: no parent")]
    public void AStandaloneMarker_Matches(string text)
    {
        Assert.True(StopMarker.IsIn(text));
    }

    [Theory]
    [InlineData("ff99747a-eb9b-40eb-af11-be19f117a74c")]
    [InlineData("d64af116-21df-4648-8270-b077b640d2ff")]
    [InlineData("LEAF11")]
    [InlineData("AF110")]
    [InlineData("AF11_draft")]
    [InlineData("x-AF11")]
    [InlineData("")]
    [InlineData(null)]
    public void AMarkerInsideAToken_DoesNotMatch(string? text)
    {
        Assert.False(StopMarker.IsIn(text));
    }

    [Fact]
    public void TheSkipReason_NamesTheMarker()
    {
        Assert.Equal("Stop term 'AF11' found in execution input", StopMarker.SKIP_REASON);
    }
}

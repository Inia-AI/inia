using IniaAi.WebApp.Components.UserWorkflows.Common;

namespace IniaAi.WebApp.Tests.Components.UserWorkflows;

public class ExecutionFailureTextTests
{
    private const string KIND = "CriticalBlockFailure";

    [Fact]
    public void HasFailure_IsFalse_WhenNeitherFieldIsPresent()
    {
        Assert.False(ExecutionFailureText.HasFailure(null, null));
        Assert.False(ExecutionFailureText.HasFailure("   ", "\t"));
    }

    [Fact]
    public void HasFailure_IsTrue_WhenOnlyTheKindWasClassified()
    {
        Assert.True(ExecutionFailureText.HasFailure(KIND, null));
    }

    [Fact]
    public void HasFailure_IsTrue_WhenOnlyTheDetailSurvived()
    {
        Assert.True(ExecutionFailureText.HasFailure(null, "something went wrong"));
    }

    [Fact]
    public void Kind_FallsBackToUnclassified_SoAFailedRowIsNeverBlank()
    {
        Assert.Equal("Unclassified", ExecutionFailureText.Kind(null));
        Assert.Equal(KIND, ExecutionFailureText.Kind(KIND));
    }

    [Fact]
    public void Summary_IsEmpty_WhenThereIsNoDetail()
    {
        Assert.Equal(string.Empty, ExecutionFailureText.Summary(null));
        Assert.Equal(string.Empty, ExecutionFailureText.Summary("   "));
    }

    [Fact]
    public void Summary_CollapsesNewlinesAndRuns_SoOneRowStaysOneLine()
    {
        Assert.Equal(
            "Block 'Agent' failed: bad request",
            ExecutionFailureText.Summary("Block 'Agent' failed:\r\n\tbad   request  "));
    }

    [Fact]
    public void Summary_ShortDetail_IsReturnedWhole()
    {
        Assert.Equal("boom", ExecutionFailureText.Summary("boom"));
    }

    [Fact]
    public void Summary_NeverExceedsTheRowBudget_EvenAtTheColumnLimit()
    {
        string maximal = new('x', 2000);

        string summary = ExecutionFailureText.Summary(maximal);

        Assert.True(
            summary.Length <= ExecutionFailureText.SUMMARY_MAX_LENGTH + 1,
            $"summary was {summary.Length} characters");
        Assert.EndsWith("…", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Summary_DoesNotSplitAWordWhenAnEarlierSpaceIsClose()
    {
        string detail = new string('a', ExecutionFailureText.SUMMARY_MAX_LENGTH - 5)
            + " overflowingword tail";

        string summary = ExecutionFailureText.Summary(detail);

        Assert.EndsWith("…", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("overflowingwor", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void IsTruncated_TellsTheUiWhetherAnythingIsHiddenBehindTheDisclosure()
    {
        Assert.False(ExecutionFailureText.IsTruncated("boom"));
        Assert.False(ExecutionFailureText.IsTruncated(null));
        Assert.True(ExecutionFailureText.IsTruncated(new string('x', 2000)));
    }

    [Fact]
    public void Full_JoinsKindAndDetail_ForTheDisclosure()
    {
        Assert.Equal($"{KIND}: boom", ExecutionFailureText.Full(KIND, "boom"));
    }

    [Fact]
    public void Full_KeepsTheDetailVerbatim_SoNothingIsLostBehindTheDisclosure()
    {
        string detail = "line one\nline two";

        Assert.Contains(detail, ExecutionFailureText.Full(KIND, detail), StringComparison.Ordinal);
    }

    [Fact]
    public void Full_OmitsTheMissingHalf()
    {
        Assert.Equal(KIND, ExecutionFailureText.Full(KIND, null));
        Assert.Equal("boom", ExecutionFailureText.Full(null, "boom"));
        Assert.Equal(string.Empty, ExecutionFailureText.Full(null, null));
    }
}

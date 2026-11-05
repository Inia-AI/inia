using System.Text.RegularExpressions;
using IniaAi.Core.Workflows.Annotations;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class NumericalGroundingTests
{
    [Theory]
    [InlineData("AS OF JANUARY 15, 2024 THE FUND HELD", "January 15, 2024")]
    [InlineData("Total: usd 5 bn committed", "USD 5 bn")]
    public void TextContainsValue_LiteralMatchIgnoresLetterCase(string text, string value)
    {
        Assert.True(NumericalGrounding.TextContainsValue(text, value));
    }

    [Fact]
    public void TextContainsValue_ValueThatIsOnlyAMultiplierWord_IsNotStatedByAnyText()
    {
        Assert.False(NumericalGrounding.TextContainsValue("Net assets were 1,204,750 at month end.", " million"));
    }

    [Theory]
    [InlineData("| | 2025<br>£'000 | 2024<br>£'000 |\n| Trade payables | **1,441** | 1,276 |")]
    [InlineData("Group statement of financial position (US$'000) and notes (€000)")]
    public void TextContainsValue_ZeroAmount_IsNotStatedByAScaleHeader(string text)
    {
        Assert.False(NumericalGrounding.TextContainsValue(text, "£0"));
        Assert.False(NumericalGrounding.StatesEquivalentValue(text, "$0"));
    }

    [Fact]
    public void TextContainsValue_ValueOnlyPresentInsideALargerNumber_IsNotStated()
    {
        Assert.False(NumericalGrounding.TextContainsValue("Net assets were 1,204,750 at month end.", "750"));
    }

    [Fact]
    public void TextContainsValue_SameCalendarDateInAnotherForm_IsStated()
    {
        Assert.True(NumericalGrounding.TextContainsValue("end date: 2011-05-01 Title: Vice President", "May 2011"));
    }

    [Fact]
    public void TextContainsValue_DifferentCalendarDate_IsNotStated()
    {
        Assert.False(NumericalGrounding.TextContainsValue("end date: 2011-06-01 Title: Vice President", "May 2011"));
    }

    [Theory]
    [InlineData("Alex Rivera, start date: 2011-05-01, Partner", "May 2011", "2011-05-01")]
    [InlineData("total net assets of $183,990 million were reported", "$184 billion", "$183,990 million")]
    public void LocateValue_ValueStatedInAnotherForm_FindsTheSpanThatStatesIt(string text, string value, string statedAs)
    {
        (int index, int length) = NumericalGrounding.LocateValue(text, value);

        Assert.Equal(statedAs, text.Substring(index, length));
    }

    [Fact]
    public void LocateValue_StrippedNumberInsideAnUnrelatedNumber_FindsTheEquivalentInstead()
    {
        const string text = "The manager was founded in 2012 and holds commitments of 12,000 million from pension plans.";

        (int index, int length) = NumericalGrounding.LocateValue(text, "12 billion");

        Assert.Equal("12,000 million", text.Substring(index, length));
    }

    [Fact]
    public void LocateValue_ValueStatedInNoForm_IsNotFound()
    {
        Assert.Equal((-1, 0), NumericalGrounding.LocateValue("total net assets were reported", "$184 billion"));
    }

    [Theory]
    [InlineData("Revenue was 15 on 2024-01-15 and 15.", "15")]
    [InlineData("A margin of 5.3 and 3 points, then 3.", "3")]
    [InlineData("13,751 units against 751 last year, 751,", "751")]
    [InlineData("100% of 100 shares, 100-day plan, 100/200 and 100.5", "100")]
    [InlineData("Revenue of £1.2m, then £1.2M, then £1.2million and x£1.2m", "£1.2m")]
    [InlineData("-5.27% fell, 5.27% rose, a5.27%, 5.27%b", "5.27%")]
    [InlineData("FY2025, 2025a, 2025_x, (2025), 2025.", "2025")]
    [InlineData("12. What drove it? 1.12 and 12,5 and 12", "12")]
    [InlineData("3.5x cover, 3.5xa, 3.5X", "3.5x")]
    [InlineData("USD 2,891.4 million and usd 2,891.4 MILLION", "USD 2,891.4 million")]
    [InlineData("31 December 2024 and 31 december 2024x", "31 December 2024")]
    [InlineData("1515151515", "1515")]
    [InlineData("q\u0301 12\u0301 12 \u200D12", "12")]
    [InlineData("\u203F12 12\u203F x\u200C12", "12")]
    public void MatchesOnNumberBoundaries_FindsTheSpansOfTheBoundaryPattern(string text, string value)
    {
        var pattern = new Regex(NumericalGrounding.WrapWithWordBoundaries(Regex.Escape(value), value), RegexOptions.IgnoreCase);

        Assert.Equal(
            pattern.Matches(text).Select(match => (match.Index, match.Length)),
            NumericalGrounding.MatchesOnNumberBoundaries(text, value));
    }
}

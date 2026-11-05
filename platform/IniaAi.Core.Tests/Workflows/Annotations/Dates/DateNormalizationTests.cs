using IniaAi.Core.Workflows.Annotations.Dates;

namespace IniaAi.Core.Tests.Workflows.Annotations.Dates;

public class DateNormalizationTests
{
    [Theory]
    [InlineData("2011-05-01", DatePrecision.Day, "2011-05-01")]
    [InlineData("2011-05-01T10:30:00Z", DatePrecision.Day, "2011-05-01")]
    [InlineData("2011/05/01", DatePrecision.Day, "2011-05-01")]
    [InlineData("1 May 2011", DatePrecision.Day, "2011-05-01")]
    [InlineData("1st of May, 2011", DatePrecision.Day, "2011-05-01")]
    [InlineData("May 1, 2011", DatePrecision.Day, "2011-05-01")]
    [InlineData("May 2011", DatePrecision.Month, "2011-05-01")]
    [InlineData("Sep. 2011", DatePrecision.Month, "2011-09-01")]
    [InlineData("2011-05", DatePrecision.Month, "2011-05-01")]
    [InlineData("25/03/2013", DatePrecision.Day, "2013-03-25")]
    public void AWrittenDate_ReadsAsTheDateItStates(string text, DatePrecision precision, string expected)
    {
        DateReading reading = DateNormalization.Read(text);

        Assert.Equal(DateReadOutcome.Read, reading.Outcome);
        Assert.Equal(new NormalizedDate(precision, DateOnly.Parse(expected), null), reading.Date);
    }

    [Fact]
    public void AnAmbiguousNumericDate_KeepsBothReadings()
    {
        DateReading reading = DateNormalization.Read("09/03/2013");

        Assert.Equal(new NormalizedDate(DatePrecision.Day, new DateOnly(2013, 9, 3), new DateOnly(2013, 3, 9)), reading.Date);
    }

    [Theory]
    [InlineData("31/02/2013")]
    [InlineData("2011-13")]
    [InlineData("May 11")]
    [InlineData("March 15")]
    [InlineData("Smarch 2011")]
    public void AValueShapedLikeADateWithNoValidReading_IsUnreadable(string text)
    {
        DateReading reading = DateNormalization.Read(text);

        Assert.Equal(new DateReading(DateReadOutcome.ShapedButUnreadable, null), reading);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("GBP 12,500,000")]
    [InlineData("signed at the offices of the Lender")]
    public void AValueThatIsNoDate_IsNotADate(string? text)
    {
        Assert.Equal(DateReadOutcome.NotADate, DateNormalization.Read(text).Outcome);
    }
}

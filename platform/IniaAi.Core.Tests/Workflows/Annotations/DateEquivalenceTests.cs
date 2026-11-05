using IniaAi.Core.Workflows.Annotations;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class DateEquivalenceTests
{
    [Theory]
    [InlineData("end date: 2011-05-01 Title: Vice President", "May 2011")]
    [InlineData("Partner since May 15, 2011", "May 2011")]
    [InlineData("Partner since 15 May 2011", "May 2011")]
    [InlineData("joined in Jun. 2005 as an analyst", "June 2005")]
    [InlineData("joined in June 2005 as an analyst", "Jun 2005")]
    [InlineData("formed in Delaware on 2013-09-03 under file number 5392925", "September 3, 2013")]
    [InlineData("formed in Delaware on 2013-09-03", "3 September 2013")]
    [InlineData("formed in Delaware on 2013-09-03", "3rd Sep 2013")]
    [InlineData("formed on September 3, 2013", "2013-09-03")]
    [InlineData("formed on 09/03/2013", "September 3, 2013")]
    [InlineData("formed on 09/03/2013", "March 9, 2013")]
    [InlineData("formed on 13/09/2013", "September 13, 2013")]
    [InlineData("formed on 09/03/2013", "2013-09-03")]
    [InlineData("first 2011-04-30, then 2011-05-31", "May 2011")]
    public void TextStatesDate_SameCalendarDateInAnotherForm_IsTrue(string text, string value)
    {
        Assert.True(DateEquivalence.TextStatesDate(text, value));
    }

    [Theory]
    [InlineData("end date: 2011-06-01 Title: Vice President", "May 2011")]
    [InlineData("end date: 2012-05-01 Title: Vice President", "May 2011")]
    [InlineData("start date year: 2011 start date month: 5", "May 2011")]
    [InlineData("formed in September 2013", "September 3, 2013")]
    [InlineData("formed on 2013-09-04", "September 3, 2013")]
    [InlineData("formed on 2013-09-03", "13/09/2013")]
    [InlineData("formed on 2013-09-03", "Q3 2013")]
    [InlineData("formed on 2013-09-03", "FY2013")]
    [InlineData("formed on 2013-09-03", "2013")]
    [InlineData("formed on 2013-09-03", "Marks 2013")]
    [InlineData("formed on 2013-09-03", "May 32, 2013")]
    [InlineData("formed on 2013-09-03", "13/13/2013")]
    [InlineData("net assets of $5 million on 2013-09-03", "$5 million")]
    [InlineData("formed on 2013-13-40", "September 3, 2013")]
    [InlineData("", "May 2011")]
    [InlineData("formed on 2013-09-03", "")]
    public void TextStatesDate_DifferentDateOrNotADate_IsFalse(string text, string value)
    {
        Assert.False(DateEquivalence.TextStatesDate(text, value));
    }

    [Theory]
    [InlineData("May 2011", "2011-05-01")]
    [InlineData("2011-05-01", "May 2011")]
    [InlineData("2011-05-01", "1 May 2011")]
    [InlineData("May 1, 2011", "2011-05-01")]
    public void AreSameDate_SameDateInTwoForms_IsTrue(string first, string second)
    {
        Assert.True(DateEquivalence.AreSameDate(first, second));
    }

    [Theory]
    [InlineData("May 2011", "June 2011")]
    [InlineData("2011-05-01", "2011-05-02")]
    [InlineData("May 2011", "May 2012")]
    [InlineData("$5 million", "May 2011")]
    [InlineData("May 2011", "")]
    public void AreSameDate_DifferentDatesOrNotDates_IsFalse(string first, string second)
    {
        Assert.False(DateEquivalence.AreSameDate(first, second));
    }
}

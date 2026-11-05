using IniaAi.Core.Workflows.Annotations;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class NumericalDataExtractorTests
{
    #region Date Extraction Tests

    [Fact]
    public void Extract_FullMonthDayYear()
    {
        List<string> result = NumericalDataExtractor.Extract("on January 15, 2024 the");
        Assert.Equal(["January 15, 2024"], result);
    }

    [Fact]
    public void Extract_FullMonthDayYearNoComma()
    {
        List<string> result = NumericalDataExtractor.Extract("on January 15 2024 the");
        Assert.Equal(["January 15 2024"], result);
    }

    [Theory]
    [InlineData("January")]
    [InlineData("February")]
    [InlineData("March")]
    [InlineData("April")]
    [InlineData("May")]
    [InlineData("June")]
    [InlineData("July")]
    [InlineData("August")]
    [InlineData("September")]
    [InlineData("October")]
    [InlineData("November")]
    [InlineData("December")]
    public void Extract_AllFullMonthNames(string month)
    {
        List<string> result = NumericalDataExtractor.Extract($"on {month} 15, 2024");
        Assert.Equal([$"{month} 15, 2024"], result);
    }

    [Fact]
    public void Extract_AbbreviatedMonthDayYear()
    {
        List<string> result = NumericalDataExtractor.Extract("on Jan 15, 2024");
        Assert.Equal(["Jan 15, 2024"], result);
    }

    [Fact]
    public void Extract_AbbreviatedMonthWithDot()
    {
        List<string> result = NumericalDataExtractor.Extract("on Jan. 15, 2024");
        Assert.Equal(["Jan. 15, 2024"], result);
    }

    [Fact]
    public void Extract_FullDayMonthYear()
    {
        // UK / international ordering. Without this pattern the day would be
        // stranded as a separate plain-number token and the month-year would
        // be picked up by MonthYearRegex, producing two tokens that can pull
        // different citations.
        List<string> result = NumericalDataExtractor.Extract("on 31 December 2024 the");
        Assert.Equal(["31 December 2024"], result);
    }

    [Fact]
    public void Extract_AbbreviatedDayMonthYear()
    {
        List<string> result = NumericalDataExtractor.Extract("on 1 Jan 2025");
        Assert.Equal(["1 Jan 2025"], result);
    }

    [Fact]
    public void Extract_AbbreviatedDayMonthWithDot()
    {
        List<string> result = NumericalDataExtractor.Extract("on 31 Dec. 2024");
        Assert.Equal(["31 Dec. 2024"], result);
    }

    [Fact]
    public void Extract_DayMonthYearWithComma()
    {
        List<string> result = NumericalDataExtractor.Extract("on 31 December, 2024");
        Assert.Equal(["31 December, 2024"], result);
    }

    [Theory]
    [InlineData("1st January 2025")]
    [InlineData("2nd Jan 2025")]
    [InlineData("3rd February 2025")]
    [InlineData("23rd December 2024")]
    [InlineData("31st December 2024")]
    [InlineData("15th May 2024")]
    public void Extract_DayMonthYearWithOrdinalSuffix(string date)
    {
        List<string> result = NumericalDataExtractor.Extract($"on {date} the");
        Assert.Equal([date], result);
    }

    [Theory]
    [InlineData("January")]
    [InlineData("February")]
    [InlineData("March")]
    [InlineData("April")]
    [InlineData("May")]
    [InlineData("June")]
    [InlineData("July")]
    [InlineData("August")]
    [InlineData("September")]
    [InlineData("October")]
    [InlineData("November")]
    [InlineData("December")]
    public void Extract_DayMonthYear_AllFullMonthNames(string month)
    {
        List<string> result = NumericalDataExtractor.Extract($"on 15 {month} 2024");
        Assert.Equal([$"15 {month} 2024"], result);
    }

    [Fact]
    public void Extract_DayMonthYear_NotSplitIntoDayAndMonthYear()
    {
        // Regression: prior to DayMonthYearRegex, this input produced
        // ["December 2024", "31"] — two separate numerical sub-facts that
        // could be attributed to different sources.
        List<string> result = NumericalDataExtractor.Extract("Net assets as of 31 December 2024 totalled");
        Assert.Equal(["31 December 2024"], result);
    }

    [Fact]
    public void Extract_FullMonthYear()
    {
        List<string> result = NumericalDataExtractor.Extract("in January 2024");
        Assert.Equal(["January 2024"], result);
    }

    [Fact]
    public void Extract_AbbreviatedMonthYear()
    {
        List<string> result = NumericalDataExtractor.Extract("in Jan 2024");
        Assert.Equal(["Jan 2024"], result);
    }

    [Fact]
    public void Extract_QuarterNotation()
    {
        List<string> result = NumericalDataExtractor.Extract("in Q3 2024");
        Assert.Equal(["Q3 2024"], result);
    }

    [Fact]
    public void Extract_QuarterNoSpace()
    {
        List<string> result = NumericalDataExtractor.Extract("in Q32024");
        Assert.Equal(["Q32024"], result);
    }

    [Theory]
    [InlineData("Q1 2024")]
    [InlineData("Q2 2024")]
    [InlineData("Q3 2024")]
    [InlineData("Q4 2024")]
    public void Extract_AllQuarters(string quarter)
    {
        List<string> result = NumericalDataExtractor.Extract($"in {quarter}");
        Assert.Equal([quarter], result);
    }

    [Fact]
    public void Extract_HalfNotation()
    {
        List<string> result = NumericalDataExtractor.Extract("in H1 2025");
        Assert.Equal(["H1 2025"], result);
    }

    [Fact]
    public void Extract_FiscalYear()
    {
        List<string> result = NumericalDataExtractor.Extract("in FY2024");
        Assert.Equal(["FY2024"], result);
    }

    [Fact]
    public void Extract_FiscalYearWithSpace()
    {
        List<string> result = NumericalDataExtractor.Extract("in FY 2024");
        Assert.Equal(["FY 2024"], result);
    }

    [Fact]
    public void Extract_IsoDate()
    {
        List<string> result = NumericalDataExtractor.Extract("on 2024-01-15");
        Assert.Equal(["2024-01-15"], result);
    }

    [Theory]
    [InlineData("Event Date 2024-07: the division was wound down.", "2024-07")]
    [InlineData("Event Date 2024-09 to 2024-10: the business was sold.", "2024-09", "2024-10")]
    public void Extract_IsoYearMonth(string text, params string[] expected)
    {
        Assert.Equal(expected, NumericalDataExtractor.Extract(text));
    }

    [Theory]
    [InlineData("| | 2025<br>£'000 | 2024<br>£'000 |", "2025", "2024")]
    [InlineData("Revenue (US$'000) 41,462", "41,462")]
    [InlineData("Revenue €000 12,300", "12,300")]
    public void Extract_ScaleHeaderZerosAreNoFigure(string text, params string[] expected)
    {
        Assert.Equal(expected, NumericalDataExtractor.Extract(text));
    }

    [Theory]
    [InlineData("in FY2024-25 revenue grew")]
    [InlineData("the 2024-2025 period")]
    public void Extract_YearRangeIsNoYearMonth(string text)
    {
        Assert.DoesNotContain(NumericalDataExtractor.Extract(text), value => value is "2024-25" or "2024-20");
    }

    [Fact]
    public void Extract_UsDate()
    {
        List<string> result = NumericalDataExtractor.Extract("on 01/15/2024");
        Assert.Equal(["01/15/2024"], result);
    }

    [Fact]
    public void Extract_YearRange()
    {
        List<string> result = NumericalDataExtractor.Extract("from 2023-2024");
        Assert.Equal(["2023-2024"], result);
    }

    [Fact]
    public void Extract_YearRangeEnDash()
    {
        List<string> result = NumericalDataExtractor.Extract("from 2023\u20132024");
        Assert.Equal(["2023\u20132024"], result);
    }

    [Fact]
    public void Extract_CalendarYear()
    {
        List<string> result = NumericalDataExtractor.Extract("in CY2024");
        Assert.Equal(["CY2024"], result);
    }

    #endregion

    #region Currency Extraction Tests

    [Fact]
    public void Extract_DollarSimple()
    {
        List<string> result = NumericalDataExtractor.Extract("was $5");
        Assert.Equal(["$5"], result);
    }

    [Fact]
    public void Extract_DollarDecimal()
    {
        List<string> result = NumericalDataExtractor.Extract("was $5.5");
        Assert.Equal(["$5.5"], result);
    }

    [Fact]
    public void Extract_DollarWithMillion()
    {
        List<string> result = NumericalDataExtractor.Extract("was $5.5 million");
        Assert.Equal(["$5.5 million"], result);
    }

    [Fact]
    public void Extract_DollarWithBillion()
    {
        List<string> result = NumericalDataExtractor.Extract("was $2.4 billion");
        Assert.Equal(["$2.4 billion"], result);
    }

    [Fact]
    public void Extract_DollarWithTrillion()
    {
        List<string> result = NumericalDataExtractor.Extract("was $1.2 trillion");
        Assert.Equal(["$1.2 trillion"], result);
    }

    [Fact]
    public void Extract_DollarWithThousand()
    {
        List<string> result = NumericalDataExtractor.Extract("was $500 thousand");
        Assert.Equal(["$500 thousand"], result);
    }

    [Fact]
    public void Extract_DollarShortM()
    {
        List<string> result = NumericalDataExtractor.Extract("was $5.5M");
        Assert.Equal(["$5.5M"], result);
    }

    [Fact]
    public void Extract_DollarShortB()
    {
        List<string> result = NumericalDataExtractor.Extract("was $2.4B");
        Assert.Equal(["$2.4B"], result);
    }

    [Fact]
    public void Extract_DollarShortBn()
    {
        List<string> result = NumericalDataExtractor.Extract("was $2.4bn");
        Assert.Equal(["$2.4bn"], result);
    }

    [Fact]
    public void Extract_DollarShortMn()
    {
        List<string> result = NumericalDataExtractor.Extract("was $5.5mn");
        Assert.Equal(["$5.5mn"], result);
    }

    [Fact]
    public void Extract_DollarShortK()
    {
        List<string> result = NumericalDataExtractor.Extract("was $500K");
        Assert.Equal(["$500K"], result);
    }

    [Fact]
    public void Extract_DollarCommaSeparated()
    {
        List<string> result = NumericalDataExtractor.Extract("was $1,234,567");
        Assert.Equal(["$1,234,567"], result);
    }

    [Fact]
    public void Extract_DollarCommaDecimal()
    {
        List<string> result = NumericalDataExtractor.Extract("was $1,234,567.89");
        Assert.Equal(["$1,234,567.89"], result);
    }

    [Fact]
    public void Extract_Euro()
    {
        List<string> result = NumericalDataExtractor.Extract("was \u20ac1.2M");
        Assert.Equal(["\u20ac1.2M"], result);
    }

    [Fact]
    public void Extract_Pound()
    {
        List<string> result = NumericalDataExtractor.Extract("was \u00a35.5 billion");
        Assert.Equal(["\u00a35.5 billion"], result);
    }

    [Fact]
    public void Extract_Yen()
    {
        List<string> result = NumericalDataExtractor.Extract("was \u00a5500B");
        Assert.Equal(["\u00a5500B"], result);
    }

    [Fact]
    public void Extract_CurrencyCodeUSD()
    {
        List<string> result = NumericalDataExtractor.Extract("was USD 1.5 million");
        Assert.Equal(["USD 1.5 million"], result);
    }

    [Fact]
    public void Extract_CurrencyCodeEUR()
    {
        List<string> result = NumericalDataExtractor.Extract("was EUR 500");
        Assert.Equal(["EUR 500"], result);
    }

    [Fact]
    public void Extract_CurrencyCodeGBP()
    {
        List<string> result = NumericalDataExtractor.Extract("was GBP 2.3bn");
        Assert.Equal(["GBP 2.3bn"], result);
    }

    [Fact]
    public void Extract_MultipleCurrencies()
    {
        List<string> result = NumericalDataExtractor.Extract("Revenue $5M and costs \u20ac3M");
        Assert.Equal(["$5M", "\u20ac3M"], result);
    }

    [Fact]
    public void Extract_NegativeDollar()
    {
        List<string> result = NumericalDataExtractor.Extract("loss of -$5.5M");
        Assert.Equal(["-$5.5M"], result);
    }

    #endregion

    #region Percentage Extraction Tests

    [Fact]
    public void Extract_SimplePercent()
    {
        List<string> result = NumericalDataExtractor.Extract("grew 15%");
        Assert.Equal(["15%"], result);
    }

    [Fact]
    public void Extract_DecimalPercent()
    {
        List<string> result = NumericalDataExtractor.Extract("grew 15.3%");
        Assert.Equal(["15.3%"], result);
    }

    [Fact]
    public void Extract_SmallPercent()
    {
        List<string> result = NumericalDataExtractor.Extract("grew 0.5%");
        Assert.Equal(["0.5%"], result);
    }

    [Fact]
    public void Extract_LargePercent()
    {
        List<string> result = NumericalDataExtractor.Extract("grew 150%");
        Assert.Equal(["150%"], result);
    }

    [Fact]
    public void Extract_PercentSpelled()
    {
        List<string> result = NumericalDataExtractor.Extract("grew 15 percent");
        Assert.Equal(["15 percent"], result);
    }

    [Fact]
    public void Extract_DecimalPercentSpelled()
    {
        List<string> result = NumericalDataExtractor.Extract("grew 15.3 percent");
        Assert.Equal(["15.3 percent"], result);
    }

    [Fact]
    public void Extract_BasisPoints()
    {
        List<string> result = NumericalDataExtractor.Extract("improved 250 basis points");
        Assert.Equal(["250 basis points"], result);
    }

    [Fact]
    public void Extract_BasisPointsShort()
    {
        List<string> result = NumericalDataExtractor.Extract("improved 250 bps");
        Assert.Equal(["250 bps"], result);
    }

    [Fact]
    public void Extract_PercentagePoints()
    {
        List<string> result = NumericalDataExtractor.Extract("up 3 percentage points");
        Assert.Equal(["3 percentage points"], result);
    }

    [Fact]
    public void Extract_PercentagePointsShort()
    {
        List<string> result = NumericalDataExtractor.Extract("up 3 pp");
        Assert.Equal(["3 pp"], result);
    }

    [Fact]
    public void Extract_NegativePercent()
    {
        List<string> result = NumericalDataExtractor.Extract("declined -15.3%");
        Assert.Equal(["-15.3%"], result);
    }

    [Fact]
    public void Extract_MultiplePercents()
    {
        List<string> result = NumericalDataExtractor.Extract("from 10% to 15%");
        Assert.Equal(["10%", "15%"], result);
    }

    [Fact]
    public void Extract_PercentRange_TwoIntegers()
    {
        // "13-14%" is a range, not a negative — both bounds must surface as
        // separate percent quantities so downstream grounding can match either.
        List<string> result = NumericalDataExtractor.Extract("insiders (13-14%)");
        Assert.Contains("13%", result);
        Assert.Contains("14%", result);
        Assert.DoesNotContain("-14%", result);
    }

    [Fact]
    public void Extract_PercentRange_TwoDecimals()
    {
        // The dash in "12.4-12.43%" is a range separator — neither bound is
        // signed.
        List<string> result = NumericalDataExtractor.Extract("ownership 12.4-12.43% range");
        Assert.Contains("12.4%", result);
        Assert.Contains("12.43%", result);
        Assert.DoesNotContain("-12.43%", result);
    }

    [Fact]
    public void Extract_PercentRange_DecimalLowerAndUpper()
    {
        // "6.26-6.263%" — both bounds must be extracted as positive percent
        // quantities. The current PercentSymbolRegex emits "-6.263%" because
        // its leading "-?" consumes the range dash; this test pins the spec.
        List<string> result = NumericalDataExtractor.Extract("State Street 6.26-6.263% stake");
        Assert.Contains("6.26%", result);
        Assert.Contains("6.263%", result);
        Assert.DoesNotContain("-6.263%", result);
    }

    [Fact]
    public void Extract_PercentRange_SingleDigitBounds()
    {
        // "(1-2%)" — hyphen between digits is a range, not a negative sign.
        List<string> result = NumericalDataExtractor.Extract("retail investors (1-2%)");
        Assert.Contains("1%", result);
        Assert.Contains("2%", result);
        Assert.DoesNotContain("-2%", result);
    }

    [Fact]
    public void Extract_PercentRange_DoesNotShadowFollowingNegative()
    {
        // After a range, an actual signed percent (separated by whitespace)
        // must still be extracted as negative.
        List<string> result = NumericalDataExtractor.Extract("range 13-14%, change -3.2%");
        Assert.Contains("13%", result);
        Assert.Contains("14%", result);
        Assert.Contains("-3.2%", result);
    }

    #endregion

    #region Plain Number Extraction Tests

    [Fact]
    public void Extract_Integer()
    {
        List<string> result = NumericalDataExtractor.Extract("grew 42 units");
        Assert.Equal(["42"], result);
    }

    [Fact]
    public void Extract_Decimal()
    {
        List<string> result = NumericalDataExtractor.Extract("ratio of 1.5");
        Assert.Equal(["1.5"], result);
    }

    [Fact]
    public void Extract_CommaSeparated()
    {
        List<string> result = NumericalDataExtractor.Extract("total of 1,234");
        Assert.Equal(["1,234"], result);
    }

    [Fact]
    public void Extract_LargeCommaSeparated()
    {
        List<string> result = NumericalDataExtractor.Extract("total of 1,234,567");
        Assert.Equal(["1,234,567"], result);
    }

    [Fact]
    public void Extract_CommaDecimal()
    {
        List<string> result = NumericalDataExtractor.Extract("total of 1,234.56");
        Assert.Equal(["1,234.56"], result);
    }

    [Fact]
    public void Extract_Multiplier_x()
    {
        List<string> result = NumericalDataExtractor.Extract("increased 2.5x");
        Assert.Equal(["2.5x"], result);
    }

    [Fact]
    public void Extract_MultipleNumbers()
    {
        List<string> result = NumericalDataExtractor.Extract("from 100 to 200");
        Assert.Equal(["100", "200"], result);
    }

    #endregion

    #region Consume-and-Exclude (Priority) Tests

    [Fact]
    public void Extract_QuarterDoesNotDoubleExtractYear()
    {
        List<string> result = NumericalDataExtractor.Extract("in Q3 2024");
        Assert.Equal(["Q3 2024"], result);
    }

    [Fact]
    public void Extract_QuarterDoesNotDoubleExtractDigit()
    {
        List<string> result = NumericalDataExtractor.Extract("in Q3 2024");
        Assert.DoesNotContain("3", result);
    }

    [Fact]
    public void Extract_FullDateDoesNotDoubleExtract()
    {
        List<string> result = NumericalDataExtractor.Extract("on January 15, 2024");
        Assert.Equal(["January 15, 2024"], result);
    }

    [Fact]
    public void Extract_CurrencyDoesNotDoubleExtractNumber()
    {
        List<string> result = NumericalDataExtractor.Extract("was $5.5 million");
        Assert.Equal(["$5.5 million"], result);
    }

    [Fact]
    public void Extract_PercentDoesNotDoubleExtractNumber()
    {
        List<string> result = NumericalDataExtractor.Extract("grew 15.3%");
        Assert.Equal(["15.3%"], result);
    }

    [Fact]
    public void Extract_YearRangeDoesNotDoubleExtract()
    {
        List<string> result = NumericalDataExtractor.Extract("from 2023-2024");
        Assert.Equal(["2023-2024"], result);
    }

    [Fact]
    public void Extract_FiscalYearDoesNotDoubleExtract()
    {
        List<string> result = NumericalDataExtractor.Extract("in FY2024");
        Assert.Equal(["FY2024"], result);
    }

    [Fact]
    public void Extract_IsoDateDoesNotDoubleExtract()
    {
        List<string> result = NumericalDataExtractor.Extract("on 2024-01-15");
        Assert.Equal(["2024-01-15"], result);
    }

    [Fact]
    public void Extract_BasisPointsDoesNotDoubleExtract()
    {
        List<string> result = NumericalDataExtractor.Extract("up 250 basis points");
        Assert.Equal(["250 basis points"], result);
    }

    [Fact]
    public void Extract_CurrencyWithCommasDoesNotDoubleExtract()
    {
        List<string> result = NumericalDataExtractor.Extract("was $1,234,567");
        Assert.Equal(["$1,234,567"], result);
    }

    #endregion

    #region Real-World Sentence Tests

    [Fact]
    public void Extract_RevenueGrowthSentence()
    {
        // Positional order: "15.3%" at 16, "$2.4 billion" at 23, "Q3 2024" at 39
        List<string> result = NumericalDataExtractor.Extract("Revenue grew by 15.3% to $2.4 billion in Q3 2024");
        Assert.Equal(["15.3%", "$2.4 billion", "Q3 2024"], result);
    }

    [Fact]
    public void Extract_IncomeReportSentence()
    {
        // Positional order: "$1,234,567" at 15, "December 31, 2024" at 47
        List<string> result = NumericalDataExtractor.Extract("Net income was $1,234,567 for the period ending December 31, 2024");
        Assert.Equal(["$1,234,567", "December 31, 2024"], result);
    }

    [Fact]
    public void Extract_YoYGrowthSentence()
    {
        List<string> result = NumericalDataExtractor.Extract("The company reported $500M in revenue, up 12% year-over-year");
        Assert.Equal(["$500M", "12%"], result);
    }

    #endregion

    #region Complex Multi-Type Extraction Tests

    [Fact]
    public void Extract_DenseFinancialSummary()
    {
        // "In FY2024, revenue reached $12.4 billion, up 18.3% from $10.5 billion in FY2023, ..."
        // Positional: FY2024(3), $12.4 billion(27), 18.3%(45), $10.5 billion(54), FY2023(71), 150 basis points(108), 32.7%(128)
        List<string> result = NumericalDataExtractor.Extract(
            "In FY2024, revenue reached $12.4 billion, up 18.3% from $10.5 billion in FY2023, with EBITDA margin expanding by 150 basis points to 32.7%");
        Assert.Equal(["FY2024", "$12.4 billion", "18.3%", "$10.5 billion", "FY2023", "150 basis points", "32.7%"], result);
    }

    [Fact]
    public void Extract_QuarterlyComparison()
    {
        // Positional: Q3 2024(0), $5.1B(19), Q3 2023(27), $4.2B(43), 21.4%(59), $1.85(95), $2.34(104)
        List<string> result = NumericalDataExtractor.Extract(
            "Q3 2024 revenue of $5.1B vs Q3 2023 revenue of $4.2B represents 21.4% growth, with EPS increasing from $1.85 to $2.34");
        Assert.Equal(["Q3 2024", "$5.1B", "Q3 2023", "$4.2B", "21.4%", "$1.85", "$2.34"], result);
    }

    [Fact]
    public void Extract_DateHeavyWithMetrics()
    {
        // Positional: January 2023(8), December 2024(25), 45.2%(57), 320 basis points(90), €2.1bn(131), €3.8bn(141)
        List<string> result = NumericalDataExtractor.Extract(
            "Between January 2023 and December 2024, the fund returned 45.2%, outperforming the benchmark by 320 basis points, with AUM growing from \u20ac2.1bn to \u20ac3.8bn");
        Assert.Equal(["January 2023", "December 2024", "45.2%", "320 basis points", "\u20ac2.1bn", "\u20ac3.8bn"], result);
    }

    [Fact]
    public void Extract_MixedCurrenciesAndDates()
    {
        // Positional: March 15, 2024(27), $750M(46), €690M(53), 12.5x(76), 25%(99), 30(117)
        List<string> result = NumericalDataExtractor.Extract(
            "The acquisition closed on March 15, 2024 for $750M (\u20ac690M), representing a 12.5x EBITDA multiple and a 25% premium to the 30-day average");
        Assert.Equal(["March 15, 2024", "$750M", "\u20ac690M", "12.5x", "25%", "30"], result);
    }

    [Fact]
    public void Extract_YearRangesWithPercentages()
    {
        // Positional: 2019-2024(9), 8.5%(33), $1.2B(67), $1.8B(76), 22.1%(108), 28.9%(117)
        List<string> result = NumericalDataExtractor.Extract(
            "Over the 2019-2024 period, CAGR was 8.5%, with revenue growing from $1.2B to $1.8B and margins expanding from 22.1% to 28.9%");
        Assert.Equal(["2019-2024", "8.5%", "$1.2B", "$1.8B", "22.1%", "28.9%"], result);
    }

    [Fact]
    public void Extract_BasisPointsAndPercentsCoexist()
    {
        // Positional: 75 basis points(24), Q4 2023(43), 5.25%(73), 15 bps(107), 1.2 percentage points(117)
        List<string> result = NumericalDataExtractor.Extract(
            "The Fed raised rates by 75 basis points in Q4 2023, pushing the yield to 5.25%, while the spread narrowed by 15 bps to 1.2 percentage points");
        Assert.Equal(["75 basis points", "Q4 2023", "5.25%", "15 bps", "1.2 percentage points"], result);
    }

    [Fact]
    public void Extract_MultiMetricDashboard()
    {
        // Positional: $3.42(7), 18.5x(26), 2.8%(48), $45.2B(68), 52(79), $28.50(97), $52.75(104)
        List<string> result = NumericalDataExtractor.Extract(
            "EPS of $3.42, P/E ratio of 18.5x, dividend yield of 2.8%, market cap of $45.2B, and 52-week range of $28.50-$52.75");
        Assert.Equal(["$3.42", "18.5x", "2.8%", "$45.2B", "52", "$28.50", "-$52.75"], result);
    }

    [Fact]
    public void Extract_TabularDataInText()
    {
        // Positional: $300M(14), 30.0%(21), $150M(38), 15.0%(45), $85M(63), 8.5%(69), $42.5M(83)
        List<string> result = NumericalDataExtractor.Extract(
            "Gross profit: $300M (30.0%); EBITDA: $150M (15.0%); Net income: $85M (8.5%); Capex: $42.5M");
        Assert.Equal(["$300M", "30.0%", "$150M", "15.0%", "$85M", "8.5%", "$42.5M"], result);
    }

    [Fact]
    public void Extract_FiscalQuarterMix()
    {
        // Positional: H1 2025(3), $6.2B(22), $3.1B(29), Q1 2025(37), $3.1B(51), Q2 2025(59), $5.4B(78), H1 2024(86), 14.8%(96), 2,500(118)
        List<string> result = NumericalDataExtractor.Extract(
            "In H1 2025, revenue was $6.2B ($3.1B in Q1 2025 and $3.1B in Q2 2025), up from $5.4B in H1 2024, a 14.8% increase driven by 2,500 new customers");
        Assert.Equal(["H1 2025", "$6.2B", "$3.1B", "Q1 2025", "$3.1B", "Q2 2025", "$5.4B", "H1 2024", "14.8%", "2,500"], result);
    }

    [Fact]
    public void Extract_IsoDateWithCurrencyAndPercent()
    {
        List<string> result = NumericalDataExtractor.Extract(
            "As of 2024-12-31, total assets were USD 8.9 billion, with a debt-to-equity ratio of 1.45 and ROE of 18.7%");
        Assert.Equal(["2024-12-31", "USD 8.9 billion", "1.45", "18.7%"], result);
    }

    [Fact]
    public void Extract_NegativeValuesAndParentheses()
    {
        // Positional: -$12.3M(19), -4.5%(28), Q1 2024(38), $8.7M(66), 2.9%(73), Q2 2024(82)
        List<string> result = NumericalDataExtractor.Extract(
            "Operating loss of -$12.3M (-4.5%) in Q1 2024 improved to profit of $8.7M (2.9%) in Q2 2024");
        Assert.Equal(["-$12.3M", "-4.5%", "Q1 2024", "$8.7M", "2.9%", "Q2 2024"], result);
    }

    [Fact]
    public void Extract_CurrencyCodesMixed()
    {
        // Positional: USD 4.2 billion(20), 55%(37), EUR 1.8 billion(43), 24%(60), GBP 850 million(66), 11%(83), JPY 78.5 billion(89), 10%(107)
        List<string> result = NumericalDataExtractor.Extract(
            "Revenue breakdown: USD 4.2 billion (55%), EUR 1.8 billion (24%), GBP 850 million (11%), JPY 78.5 billion (10%)");
        Assert.Equal(["USD 4.2 billion", "55%", "EUR 1.8 billion", "24%", "GBP 850 million", "11%", "JPY 78.5 billion", "10%"], result);
    }

    [Fact]
    public void Extract_AllTypesInOneSentence()
    {
        // Positional order in text
        List<string> result = NumericalDataExtractor.Extract(
            "On January 15, 2024, the company announced Q4 2023 results: revenue of $2.4B (+15.3%), with FY2023 net income up 250 basis points to \u20ac1.8 billion across 12,500 customers in the 2022-2023 fiscal transition");
        Assert.Equal([
            "January 15, 2024", "Q4 2023",
            "$2.4B", "15.3%",
            "FY2023", "250 basis points",
            "\u20ac1.8 billion", "12,500",
            "2022-2023"
        ], result);
    }

    [Fact]
    public void Extract_ComparisonSentence()
    {
        // Positional: $4.2B(23), Q1 2023(31), $5.1B(42), Q1 2024(50)
        List<string> result = NumericalDataExtractor.Extract(
            "Revenue increased from $4.2B in Q1 2023 to $5.1B in Q1 2024");
        Assert.Equal(["$4.2B", "Q1 2023", "$5.1B", "Q1 2024"], result);
    }

    [Fact]
    public void Extract_MarginImprovementSentence()
    {
        List<string> result = NumericalDataExtractor.Extract(
            "In FY2024, EBITDA margin improved by 250 basis points to 35.2%");
        Assert.Equal(["FY2024", "250 basis points", "35.2%"], result);
    }

    [Fact]
    public void Extract_MultiMetricSentence()
    {
        List<string> result = NumericalDataExtractor.Extract(
            "EPS of $3.42, P/E ratio of 18.5x, and dividend yield of 2.8%");
        Assert.Equal(["$3.42", "18.5x", "2.8%"], result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void Extract_EmptyString()
    {
        List<string> result = NumericalDataExtractor.Extract("");
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_NullString()
    {
        List<string> result = NumericalDataExtractor.Extract(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_WhitespaceOnly()
    {
        List<string> result = NumericalDataExtractor.Extract("   ");
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_NoNumbers()
    {
        List<string> result = NumericalDataExtractor.Extract("The company expanded into European markets");
        Assert.Empty(result);
    }

    [Fact]
    public void Extract_SingleNumberIsFullText()
    {
        List<string> result = NumericalDataExtractor.Extract("15.3%");
        Assert.Equal(["15.3%"], result);
    }

    [Fact]
    public void Extract_OnlyCurrencyIsFullText()
    {
        List<string> result = NumericalDataExtractor.Extract("$2.4 billion");
        Assert.Equal(["$2.4 billion"], result);
    }

    [Fact]
    public void Extract_NumberInParentheses()
    {
        List<string> result = NumericalDataExtractor.Extract("revenue ($5.5M)");
        Assert.Equal(["$5.5M"], result);
    }

    [Fact]
    public void Extract_PreservesOrder()
    {
        // Positional order: $5M(0), 15%(10), Q3 2024(19)
        List<string> result = NumericalDataExtractor.Extract("$5M then 15% then Q3 2024");
        Assert.Equal(["$5M", "15%", "Q3 2024"], result);
    }

    [Fact]
    public void Extract_DuplicateValues()
    {
        List<string> result = NumericalDataExtractor.Extract("$5M and $5M");
        Assert.Equal(["$5M", "$5M"], result);
    }

    [Fact]
    public void Extract_AdjacentNumbers()
    {
        List<string> result = NumericalDataExtractor.Extract("$5M/$3M net");
        Assert.Equal(["$5M", "$3M"], result);
    }

    #endregion
}

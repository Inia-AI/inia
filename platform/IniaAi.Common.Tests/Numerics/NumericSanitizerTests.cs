using IniaAi.Common.Numerics;

namespace IniaAi.Common.Tests.Numerics;

public class NumericSanitizerTests
{
    // ── ExtractInt: Clean inputs ──

    [Theory]
    [InlineData("17", 17)]
    [InlineData(" 42 ", 42)]
    [InlineData("-5", -5)]
    [InlineData("0", 0)]
    public void ExtractInt_CleanInputs(string input, int expected)
    {
        Assert.Equal(expected, NumericSanitizer.ExtractInt(input));
    }

    // ── ExtractInt: LLM-style prefixes/suffixes ──

    [Theory]
    [InlineData("page17", 17)]
    [InlineData("Page 17", 17)]
    [InlineData("page 17", 17)]
    [InlineData("pg 3", 3)]
    [InlineData("$500", 500)]
    [InlineData("Result: 42", 42)]
    public void ExtractInt_LlmPrefixesAndSuffixes(string input, int expected)
    {
        Assert.Equal(expected, NumericSanitizer.ExtractInt(input));
    }

    // ── ExtractInt: Ranges, lists, mixed content (first number wins) ──

    [Theory]
    [InlineData("17-19", 17)]
    [InlineData("pages 4 and 6", 4)]
    [InlineData("7,12-15,18", 7)]
    [InlineData("3, No data available, 17", 3)]
    [InlineData("No data available, 5, 12", 5)]
    [InlineData("10, 20, No data available", 10)]
    [InlineData("page 3 of 25", 3)]
    public void ExtractInt_MixedContent_FirstNumberWins(string input, int expected)
    {
        Assert.Equal(expected, NumericSanitizer.ExtractInt(input));
    }

    // ── ExtractInt: Decimal inputs (truncated) ──

    [Theory]
    [InlineData("3.7", 3)]
    [InlineData("page 2.5", 2)]
    public void ExtractInt_DecimalInputs_Truncated(string input, int expected)
    {
        Assert.Equal(expected, NumericSanitizer.ExtractInt(input));
    }

    // ── ExtractInt: Null/empty/no-number (returns null) ──

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no numbers")]
    [InlineData("No data available")]
    [InlineData("N/A")]
    [InlineData("abc")]
    public void ExtractInt_NullEmptyNoNumber_ReturnsNull(string? input)
    {
        Assert.Null(NumericSanitizer.ExtractInt(input));
    }

    // ── ExtractIntList: Clean lists and ranges ──

    [Fact]
    public void ExtractIntList_SingleNumber()
    {
        Assert.Equal([7], NumericSanitizer.ExtractIntList("7"));
    }

    [Fact]
    public void ExtractIntList_CommaSeparated()
    {
        Assert.Equal([7, 12, 18], NumericSanitizer.ExtractIntList("7,12,18"));
    }

    [Fact]
    public void ExtractIntList_HyphenRange()
    {
        Assert.Equal([12, 13, 14, 15], NumericSanitizer.ExtractIntList("12-15"));
    }

    [Fact]
    public void ExtractIntList_MixedListAndRange()
    {
        Assert.Equal([7, 12, 13, 14, 15, 18], NumericSanitizer.ExtractIntList("7,12-15,18"));
    }

    [Fact]
    public void ExtractIntList_MultipleRanges()
    {
        Assert.Equal([1, 2, 3, 7, 10, 11, 12], NumericSanitizer.ExtractIntList("1-3,7,10-12"));
    }

    // ── ExtractIntList: Dash variants ──

    [Fact]
    public void ExtractIntList_EnDash()
    {
        Assert.Equal([12, 13, 14, 15], NumericSanitizer.ExtractIntList("12\u201315"));
    }

    [Fact]
    public void ExtractIntList_EmDash()
    {
        Assert.Equal([12, 13, 14, 15], NumericSanitizer.ExtractIntList("12\u201415"));
    }

    [Fact]
    public void ExtractIntList_MinusSign()
    {
        Assert.Equal([12, 13, 14, 15], NumericSanitizer.ExtractIntList("12\u221215"));
    }

    [Fact]
    public void ExtractIntList_UnicodeHyphen()
    {
        Assert.Equal([12, 13, 14, 15], NumericSanitizer.ExtractIntList("12\u201015"));
    }

    [Fact]
    public void ExtractIntList_NonBreakingHyphen()
    {
        Assert.Equal([12, 13, 14, 15], NumericSanitizer.ExtractIntList("12\u201115"));
    }

    // ── ExtractIntList: LLM noise in segments ──

    [Fact]
    public void ExtractIntList_PrefixOnWholeInput()
    {
        Assert.Equal([7, 12, 13, 14, 15, 18], NumericSanitizer.ExtractIntList("pages 7,12-15,18"));
    }

    [Fact]
    public void ExtractIntList_SinglePrefixedValue()
    {
        Assert.Equal([3], NumericSanitizer.ExtractIntList("Page 3"));
    }

    [Fact]
    public void ExtractIntList_WhitespaceTolerance()
    {
        Assert.Equal([1, 2, 3], NumericSanitizer.ExtractIntList(" 1, 2 , 3 "));
    }

    [Fact]
    public void ExtractIntList_PrefixOnFirstSegmentOnly()
    {
        Assert.Equal([3, 17], NumericSanitizer.ExtractIntList("page17, 3"));
    }

    [Fact]
    public void ExtractIntList_AllSegmentsPrefixed()
    {
        Assert.Equal([1, 2, 3], NumericSanitizer.ExtractIntList("page 1, page 2, page 3"));
    }

    [Fact]
    public void ExtractIntList_NoSpacePrefixNoSpacesBetween()
    {
        Assert.Equal([17, 19], NumericSanitizer.ExtractIntList("Page17,Page19"));
    }

    [Fact]
    public void ExtractIntList_PrefixedRangeAndPrefixedSingle()
    {
        Assert.Equal([1, 2, 3, 7], NumericSanitizer.ExtractIntList("pg 1-3, pg 7"));
    }

    [Fact]
    public void ExtractIntList_MixedPrefixCleanWithRange()
    {
        Assert.Equal([1, 3, 4, 5, 8], NumericSanitizer.ExtractIntList("page 1, 3-5, page 8"));
    }

    [Fact]
    public void ExtractIntList_RangeWithPrefixesOnBothSides()
    {
        Assert.Equal([1, 2, 3], NumericSanitizer.ExtractIntList("page 1 - page 3"));
    }

    // ── ExtractIntList: Null/empty/invalid (returns empty list) ──

    [Fact]
    public void ExtractIntList_Null_ReturnsEmpty()
    {
        Assert.Empty(NumericSanitizer.ExtractIntList(null));
    }

    [Fact]
    public void ExtractIntList_Empty_ReturnsEmpty()
    {
        Assert.Empty(NumericSanitizer.ExtractIntList(""));
    }

    [Fact]
    public void ExtractIntList_NoDataAvailable_ReturnsEmpty()
    {
        Assert.Empty(NumericSanitizer.ExtractIntList("No data available"));
    }

    [Fact]
    public void ExtractIntList_NA_ReturnsEmpty()
    {
        Assert.Empty(NumericSanitizer.ExtractIntList("N/A"));
    }

    // ── ExtractIntList: Edge cases ──

    [Fact]
    public void ExtractIntList_SameStartAndEnd()
    {
        Assert.Equal([5], NumericSanitizer.ExtractIntList("5-5"));
    }

    [Fact]
    public void ExtractIntList_ReversedRange_AutoCorrects()
    {
        Assert.Equal([12, 13, 14, 15], NumericSanitizer.ExtractIntList("15-12"));
    }

    [Fact]
    public void ExtractIntList_SkipsNonNumericSegments()
    {
        Assert.Equal([3, 17], NumericSanitizer.ExtractIntList("3, No data available, 17"));
    }

    // ── ExtractDouble: Happy path ──

    [Theory]
    [InlineData("3.14", 3.14)]
    [InlineData("page 2.5", 2.5)]
    [InlineData("42", 42.0)]
    [InlineData("-0.5", -0.5)]
    [InlineData("$99.99", 99.99)]
    public void ExtractDouble_HappyPath(string input, double expected)
    {
        Assert.Equal(expected, NumericSanitizer.ExtractDouble(input));
    }

    // ── ExtractDouble: Null/empty/no-number (returns null) ──

    [Theory]
    [InlineData(null)]
    [InlineData("No data available")]
    public void ExtractDouble_NullOrNoNumber_ReturnsNull(string? input)
    {
        Assert.Null(NumericSanitizer.ExtractDouble(input));
    }

    // ── StripCommonListNoise: brackets, quotes, "page" word ──

    [Theory]
    [InlineData("[1,2]", "1,2")]
    [InlineData("(1, 2, 3)", "1, 2, 3")]
    [InlineData("{1, 2, 3-5}", "1, 2, 3-5")]
    [InlineData("[\"1\",\"2\"]", "1,2")]
    [InlineData("'1','2'", "1,2")]
    [InlineData("Page 7", " 7")]
    [InlineData("pages 1-3", " 1-3")]
    [InlineData("PAGE 4", " 4")]
    [InlineData("Page 1, page 2", " 1,  2")]
    [InlineData("homepage 7", "home 7")]
    public void StripCommonListNoise_RemovesKnownNoise(string input, string expected)
    {
        Assert.Equal(expected, NumericSanitizer.StripCommonListNoise(input));
    }

    [Theory]
    [InlineData("1,2,3", "1,2,3")]
    [InlineData("1-5", "1-5")]
    [InlineData("-1-", "-1-")]
    [InlineData("1abc2", "1abc2")]
    [InlineData("500.50", "500.50")]
    [InlineData("!@#", "!@#")]
    public void StripCommonListNoise_LeavesEverythingElseAlone(string input, string expected)
    {
        Assert.Equal(expected, NumericSanitizer.StripCommonListNoise(input));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void StripCommonListNoise_NullOrEmpty_ReturnsEmpty(string? input, string expected)
    {
        Assert.Equal(expected, NumericSanitizer.StripCommonListNoise(input));
    }
}

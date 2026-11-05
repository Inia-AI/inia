using System.Globalization;
using IniaAi.FileFormats.Excel;

namespace IniaAi.FileFormats.Tests.Excel;

public class ExcelNumberTextTests
{
    private const string GENERAL = "General";

    private static readonly CultureInfo _usEnglish = CultureInfo.GetCultureInfo("en-US");

    [Theory]
    [InlineData(0d, "")]
    [InlineData(1500d, "1,500")]
    [InlineData(-1500d, "-1,500")]
    public void Format_ShowsNothing_ForAnEmptySection(double value, string expected)
    {
        Assert.Equal(expected, Format(value, "#,##0;\\-#,##0;"));
    }

    [Theory]
    [InlineData(5d, "Yes")]
    [InlineData(-1d, "")]
    [InlineData(0d, "No")]
    public void Format_ChoosesTheSectionBySign_WhenASectionIsEmpty(double value, string expected)
    {
        Assert.Equal(expected, Format(value, "\"Yes\";;\"No\""));
    }

    [Fact]
    public void Format_ShowsANegativeValueInItsOwnSection_WithoutASign()
    {
        Assert.Equal("(1,500)", Format(-1500, "#,##0;(#,##0)"));
    }

    [Theory]
    [InlineData(0.125, "0.00", "0.13")]
    [InlineData(2.5, "0", "3")]
    [InlineData(-2.5, "0", "-3")]
    [InlineData(1.005, "0.00", "1.01")]
    [InlineData(1234.5, "#,##0", "1,235")]
    [InlineData(0.0125, "0.0%", "1.3%")]
    [InlineData(0.00125, "0.00%", "0.13%")]
    [InlineData(12500, "#,##0,\"K\"", "13K")]
    public void Format_RoundsAHalfwayValueAwayFromZero(double value, string format, string expected)
    {
        Assert.Equal(expected, Format(value, format));
    }

    [Fact]
    public void Format_ShowsAValueThatRoundsToZero_WithoutAMinusSign()
    {
        Assert.Equal("0.00", Format(-0.001, "0.00"));
    }

    [Theory]
    [InlineData(0d, "0")]
    [InlineData(1.5, "1.5")]
    [InlineData(12345678901d, "12345678901")]
    [InlineData(123456789012d, "1.23457E+11")]
    [InlineData(-123456789012d, "-1.23457E+11")]
    [InlineData(0.333333333333333, "0.333333333")]
    [InlineData(1234.56789012, "1234.56789")]
    [InlineData(0.00001234, "0.00001234")]
    [InlineData(0.000000001234, "1.234E-09")]
    public void Format_ShowsTheGeneralFormatInElevenCharacters(double value, string expected)
    {
        Assert.Equal(expected, Format(value, GENERAL));
    }

    [Fact]
    public void Format_ShowsADateInItsFormat()
    {
        Assert.Equal("2024-12-31", Format(45657, "yyyy-mm-dd"));
    }

    [Theory]
    [InlineData(-1d)]
    [InlineData(2958470d)]
    public void Format_FillsADateOutsideExcelsRange_WithHashes(double value)
    {
        Assert.Equal("########", Format(value, "yyyy-mm-dd"));
    }

    [Fact]
    public void Format_ShowsALetterOutsideQuotes_AsItself()
    {
        Assert.Equal("1.3x", Format(1.25, "0.0x"));
    }

    [Fact]
    public void Format_ShowsMinutesSecondsAndTenths()
    {
        Assert.Equal("01:30.5", Format(90.5 / 86400, "mm:ss.0"));
    }

    private static string Format(double value, string format)
    {
        return ExcelNumberText.Format(value, format, isDate1904: false, _usEnglish);
    }
}

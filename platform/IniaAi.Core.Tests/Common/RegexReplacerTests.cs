using System.Text.RegularExpressions;
using IniaAi.Core.Common;

namespace IniaAi.Core.Tests.Common;

public class RegexReplacerTests
{
    // ── Literal replace ─────────────────────────────────────────────────────────

    [Fact]
    public void Replace_Literal_BasicSubstitution()
    {
        string result = RegexReplacer.Replace("hello world", "world", "there", isRegex: false, caseSensitive: true);
        Assert.Equal("hello there", result);
    }

    [Fact]
    public void Replace_Literal_CaseSensitive_NoMatchLeavesUnchanged()
    {
        string result = RegexReplacer.Replace("Hello", "hello", "x", isRegex: false, caseSensitive: true);
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void Replace_Literal_CaseInsensitive_Matches()
    {
        string result = RegexReplacer.Replace("Hello", "hello", "x", isRegex: false, caseSensitive: false);
        Assert.Equal("x", result);
    }

    [Fact]
    public void Replace_EmptyFind_ReturnsInputUnchanged()
    {
        string result = RegexReplacer.Replace("untouched", "", "x", isRegex: true, caseSensitive: true);
        Assert.Equal("untouched", result);
    }

    // ── Regex replace ───────────────────────────────────────────────────────────

    [Fact]
    public void Replace_Regex_CaptureGroups()
    {
        string result = RegexReplacer.Replace("price: 1250.00 USD", @"(\d+)\.(\d{2})", "$1 dollars $2 cents", isRegex: true, caseSensitive: true);
        Assert.Equal("price: 1250 dollars 00 cents USD", result);
    }

    [Fact]
    public void Replace_Regex_CaseInsensitive()
    {
        string result = RegexReplacer.Replace("ABCabc", "abc", "_", isRegex: true, caseSensitive: false);
        Assert.Equal("__", result);
    }

    // ── Whitespace token normalization ──────────────────────────────────────────

    [Fact]
    public void Replace_NormalizesTokens_InReplaceWith()
    {
        string result = RegexReplacer.Replace("a,b", ",", RegexReplacer.TOKEN_NEWLINE, isRegex: false, caseSensitive: true);
        Assert.Equal("a\nb", result);
    }

    [Fact]
    public void Replace_NormalizesTokens_InFind()
    {
        string result = RegexReplacer.Replace("a\tb", RegexReplacer.TOKEN_TAB, " ", isRegex: false, caseSensitive: true);
        Assert.Equal("a b", result);
    }

    [Fact]
    public void NormalizeTokens_AllTokens()
    {
        string result = RegexReplacer.NormalizeTokens(
            $"{RegexReplacer.TOKEN_NEWLINE}{RegexReplacer.TOKEN_TAB}{RegexReplacer.TOKEN_SPACE}{RegexReplacer.TOKEN_RETURN}");
        Assert.Equal("\n\t \r", result);
    }

    // ── Failure modes (caller decides handling) ─────────────────────────────────

    [Fact]
    public void Replace_InvalidRegexPattern_Throws()
    {
        // .NET throws RegexParseException, which derives from ArgumentException — what the callers catch.
        _ = Assert.ThrowsAny<ArgumentException>(() =>
            RegexReplacer.Replace("input", "(unclosed", "x", isRegex: true, caseSensitive: true));
    }

    [Fact]
    public void Replace_CatastrophicBacktracking_ThrowsTimeout()
    {
        // Classic catastrophic-backtracking pattern against input that never matches the trailing b.
        _ = Assert.Throws<RegexMatchTimeoutException>(() =>
            RegexReplacer.Replace(new string('a', 40) + "!", "(a+)+b", "x", isRegex: true, caseSensitive: true));
    }

    // ── Canonical digit-group separator strip (the workflow use case) ────────────

    [Fact]
    public void Replace_StripsInterDigitCommasOnly_PreservesPropertySeparators()
    {
        // (?<=\d),(?=\d) removes commas between digits but leaves the JSON property-separator comma.
        string result = RegexReplacer.Replace("""{"a":1,54,000,"b":2}""", @"(?<=\d),(?=\d)", "", isRegex: true, caseSensitive: true);
        Assert.Equal("""{"a":154000,"b":2}""", result);
    }

    [Fact]
    public void Replace_DigitGroupStrip_PreservesDecimalPoint()
    {
        string result = RegexReplacer.Replace("1,234.56", @"(?<=\d),(?=\d)", "", isRegex: true, caseSensitive: true);
        Assert.Equal("1234.56", result);
    }

    [Fact]
    public void Replace_DigitGroupStrip_IndianGrouping()
    {
        string result = RegexReplacer.Replace("1,54,03,04,063", @"(?<=\d),(?=\d)", "", isRegex: true, caseSensitive: true);
        Assert.Equal("1540304063", result);
    }
}

using IniaAi.Common.Numerics;

namespace IniaAi.Common.Tests.Numerics;

public class MagnitudeParserTests
{
    [Theory]
    [InlineData("units", 1)]
    [InlineData("Units", 1)]
    [InlineData("million", 1e6)]
    [InlineData(" bn ", 1e9)]
    public void TryGetMultiplier_RecognisedWord_ReturnsItsFactor(string suffix, double expected)
    {
        Assert.True(MagnitudeParser.TryGetMultiplier(suffix, out double factor));
        Assert.Equal(expected, factor);
    }

    [Theory]
    [InlineData("widgets")]
    [InlineData("")]
    [InlineData(null)]
    public void TryGetMultiplier_UnknownOrMissingWord_IsNotRecognised(string? suffix)
    {
        Assert.False(MagnitudeParser.TryGetMultiplier(suffix, out _));
    }

    [Theory]
    [InlineData("trillion", 1e12)]
    [InlineData("TRILLION", 1e12)]
    [InlineData("Trillion", 1e12)]
    [InlineData("t", 1e12)]
    [InlineData("T", 1e12)]
    public void GetMultiplier_Trillions(string suffix, double expected)
    {
        Assert.Equal(expected, MagnitudeParser.GetMultiplier(suffix));
    }

    [Theory]
    [InlineData("billion", 1e9)]
    [InlineData("BILLION", 1e9)]
    [InlineData("Billion", 1e9)]
    [InlineData("billions", 1e9)]
    [InlineData("Billions", 1e9)]
    [InlineData("b", 1e9)]
    [InlineData("B", 1e9)]
    [InlineData("bn", 1e9)]
    [InlineData("Bn", 1e9)]
    [InlineData("BN", 1e9)]
    [InlineData("bln", 1e9)]
    [InlineData("BLN", 1e9)]
    public void GetMultiplier_Billions(string suffix, double expected)
    {
        Assert.Equal(expected, MagnitudeParser.GetMultiplier(suffix));
    }

    [Theory]
    [InlineData("million", 1e6)]
    [InlineData("MILLION", 1e6)]
    [InlineData("Million", 1e6)]
    [InlineData("millions", 1e6)]
    [InlineData("Millions", 1e6)]
    [InlineData("m", 1e6)]
    [InlineData("M", 1e6)]
    [InlineData("mm", 1e6)]
    [InlineData("MM", 1e6)]
    [InlineData("mn", 1e6)]
    [InlineData("Mn", 1e6)]
    [InlineData("MN", 1e6)]
    [InlineData("mln", 1e6)]
    [InlineData("MLN", 1e6)]
    public void GetMultiplier_Millions(string suffix, double expected)
    {
        Assert.Equal(expected, MagnitudeParser.GetMultiplier(suffix));
    }

    [Theory]
    [InlineData("thousand", 1e3)]
    [InlineData("THOUSAND", 1e3)]
    [InlineData("Thousand", 1e3)]
    [InlineData("thousands", 1e3)]
    [InlineData("Thousands", 1e3)]
    [InlineData("k", 1e3)]
    [InlineData("K", 1e3)]
    [InlineData("th", 1e3)]
    [InlineData("TH", 1e3)]
    [InlineData("Th", 1e3)]
    public void GetMultiplier_Thousands(string suffix, double expected)
    {
        Assert.Equal(expected, MagnitudeParser.GetMultiplier(suffix));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("units")]
    [InlineData("Units")]
    [InlineData("UNITS")]
    [InlineData("foo")]
    [InlineData("xyz123")]
    [InlineData("dollars")]
    [InlineData("%")]
    public void GetMultiplier_ReturnsOneForUnrecognized(string? suffix)
    {
        Assert.Equal(1.0, MagnitudeParser.GetMultiplier(suffix));
    }

    [Theory]
    [InlineData(" billion ", 1e9)]
    [InlineData("  M  ", 1e6)]
    [InlineData("\tbillion\t", 1e9)]
    [InlineData(" k ", 1e3)]
    [InlineData("  units  ", 1.0)]
    public void GetMultiplier_TrimsWhitespace(string suffix, double expected)
    {
        Assert.Equal(expected, MagnitudeParser.GetMultiplier(suffix));
    }

    [Theory]
    [InlineData("trillion", 1_000_000_000_000)]
    [InlineData("billion", 1_000_000_000)]
    [InlineData("million", 1_000_000)]
    [InlineData("thousand", 1_000)]
    [InlineData("bn", 1_000_000_000)]
    [InlineData("mln", 1_000_000)]
    [InlineData("k", 1_000)]
    [InlineData("mm", 1_000_000)]
    [InlineData("th", 1_000)]
    public void GetMultiplierAsDecimal_ReturnsCorrectDecimal(string suffix, decimal expected)
    {
        Assert.Equal(expected, MagnitudeParser.GetMultiplierAsDecimal(suffix));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("units")]
    [InlineData("garbage")]
    public void GetMultiplierAsDecimal_ReturnsOneForUnrecognized(string? suffix)
    {
        Assert.Equal(1m, MagnitudeParser.GetMultiplierAsDecimal(suffix));
    }

    [Fact]
    public void SuffixFactors_IsNotEmpty()
    {
        Assert.NotEmpty(MagnitudeParser.SuffixFactors);
    }

    [Fact]
    public void SuffixFactors_AllFactorsArePositive()
    {
        foreach ((string suffix, double factor) in MagnitudeParser.SuffixFactors)
        {
            Assert.True(factor > 0, $"Factor for suffix '{suffix}' should be positive, was {factor}");
        }
    }

    [Fact]
    public void SuffixFactors_OrderedLongestFirst()
    {
        // Word suffixes (starting with space) should come before short suffixes
        // and longer suffixes before shorter ones within each group.
        // This is needed for regex matching (EndsWith) to prefer the longest match.
        int lastWordSuffixIndex = -1;
        int firstShortSuffixIndex = int.MaxValue;

        for (int i = 0; i < MagnitudeParser.SuffixFactors.Length; i++)
        {
            string suffix = MagnitudeParser.SuffixFactors[i].Suffix;
            if (suffix.StartsWith(' '))
            {
                lastWordSuffixIndex = i;
            }
            else if (firstShortSuffixIndex == int.MaxValue)
            {
                firstShortSuffixIndex = i;
            }
        }

        Assert.True(lastWordSuffixIndex < firstShortSuffixIndex,
            "Word suffixes (with leading space) should come before short suffixes");
    }

    [Fact]
    public void SuffixFactors_ContainsAllMagnitudes()
    {
        double[] expectedFactors = [1e12, 1e9, 1e6, 1e3];
        foreach (double expected in expectedFactors)
        {
            Assert.Contains(MagnitudeParser.SuffixFactors,
                sf => sf.Factor == expected);
        }
    }

    [Fact]
    public void SuffixFactors_TwoCharSuffixesBeforeSingleChar()
    {
        // "Mn" must come before "M", "Bn" before "B", etc.
        // to prevent premature matching in EndsWith checks
        var suffixes = MagnitudeParser.SuffixFactors
            .Where(sf => !sf.Suffix.StartsWith(' '))
            .Select(sf => sf.Suffix)
            .ToList();

        int IndexOf(string s) => suffixes.IndexOf(s);

        Assert.True(IndexOf("Mn") < IndexOf("M"), "Mn should come before M");
        Assert.True(IndexOf("Bn") < IndexOf("B"), "Bn should come before B");
        Assert.True(IndexOf("mn") < IndexOf("m"), "mn should come before m");
        Assert.True(IndexOf("bn") < IndexOf("b"), "bn should come before b");
    }

    [Fact]
    public void GetMultiplier_CoversAllKnownSuffixes()
    {
        // Every suffix that appears in SuffixFactors (stripped of leading space)
        // should return the correct factor via GetMultiplier
        foreach ((string suffix, double factor) in MagnitudeParser.SuffixFactors)
        {
            string cleaned = suffix.TrimStart();
            Assert.Equal(factor, MagnitudeParser.GetMultiplier(cleaned));
        }
    }

    [Theory]
    [InlineData("billions", 1e9)]   // TemplateBasedExcelService
    [InlineData("millions", 1e6)]   // TemplateBasedExcelService
    [InlineData("thousands", 1e3)]  // TemplateBasedExcelService
    [InlineData("mm", 1e6)]         // HedgeFundsMonitoringObservationsParser
    [InlineData("th", 1e3)]         // HedgeFundsMonitoringObservationsParser
    [InlineData("bln", 1e9)]        // UnifiedFactComparisonService
    [InlineData("mln", 1e6)]        // UnifiedFactComparisonService
    public void GetMultiplier_AllCallerSuffixVariantsWork(string suffix, double expected)
    {
        Assert.Equal(expected, MagnitudeParser.GetMultiplier(suffix));
    }
}

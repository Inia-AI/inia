using IniaAi.Core.Workflows.Annotations.Quantities;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class UnitExtractorTests
{
    [Fact]
    public void Extract_DollarBillionPhrase_YieldsUsdCurrencyWithMagnitude()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "Apple reported $300 billion in revenue for fiscal year 2024.");

        Quantity? apple = quantities.FirstOrDefault(q =>
            q.Kind is UnitKind.Currency && q.Subtype == "USD");

        Assert.NotNull(apple);
        Assert.Equal(300, apple.Value);
        Assert.Equal(1e9, apple.Multiplier);
        Assert.Equal(3e11, apple.BaseValue);
        Assert.Equal("$300 billion", apple.Surface);
    }

    [Fact]
    public void Extract_CelsiusReading_YieldsTemperatureKind()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "The temperature reached 85.5°C in the reactor core.");

        Quantity? celsius = quantities.FirstOrDefault(q =>
            q.Kind is UnitKind.Temperature && q.Subtype == "temperature-celsius");

        Assert.NotNull(celsius);
        Assert.Equal(85.5, celsius.Value, 0.001);
    }

    [Fact]
    public void Extract_IsoCurrencyCodeWithTrillion_YieldsCurrencyWithCorrectMagnitude()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "Total assets stand at USD 1.2 trillion across the portfolio.");

        Quantity? usd = quantities.FirstOrDefault(q =>
            q.Kind is UnitKind.Currency && q.Subtype == "USD");

        Assert.NotNull(usd);
        Assert.Equal(1.2, usd.Value, 0.001);
        Assert.Equal(1.2e12, usd.BaseValue, 1);
    }

    [Fact]
    public void Extract_ParenthesizedPercent_YieldsConcentrationKind()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "Three institutional investors hold 88,596,463 shares (88.7%).");

        Quantity? share = quantities.FirstOrDefault(q =>
            q.Kind is UnitKind.Concentration && q.Subtype == "concentr-percent");

        Assert.NotNull(share);
        Assert.Equal(-88.7, share.Value, 0.001);
    }

    [Fact]
    public void Extract_BasisPoints_YieldsConcentrationWithDistinctSubtype()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "Set the threshold to 25 basis points above the benchmark.");

        Quantity? bp = quantities.FirstOrDefault(q =>
            q.Subtype == "concentr-basis-point");

        Assert.NotNull(bp);
        Assert.Equal(25, bp.Value);
    }

    [Fact]
    public void Extract_KilogramSiAbbreviation_YieldsMassKind()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "Approximately 75 kg of materials processed per hour.");

        Quantity? kg = quantities.FirstOrDefault(q =>
            q.Kind is UnitKind.Mass && q.Subtype == "mass-kilogram");

        Assert.NotNull(kg);
        Assert.Equal(75, kg.Value);
    }

    [Fact]
    public void Extract_EuroIsoWithMillionMagnitude_YieldsEurCurrency()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "EUR 50 million was allocated to R&D this year.");

        Quantity? eur = quantities.FirstOrDefault(q =>
            q.Kind is UnitKind.Currency && q.Subtype == "EUR");

        Assert.NotNull(eur);
        Assert.Equal(50, eur.Value);
        Assert.Equal(5e7, eur.BaseValue, 1);
    }

    [Fact]
    public void Extract_AustralianDollarPrefixSymbol_YieldsAudCurrency()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "The fund holds A$2.4 billion in Australian equities.");

        Quantity? aud = quantities.FirstOrDefault(q =>
            q.Kind is UnitKind.Currency && q.Subtype == "AUD");

        Assert.NotNull(aud);
        Assert.Equal(2.4e9, aud.BaseValue, 1);
    }

    [Fact]
    public void Extract_BareDollarsWordWithMagnitude_YieldsUsdCurrency()
    {
        // Magnitude word sits between number and unit; magnitude-first pass must
        // advance past it so "dollars" can still pair with the number.
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "Total revenue of 754 million dollars across all segments.");

        Quantity? usd = quantities.FirstOrDefault(q =>
            q.Kind is UnitKind.Currency && q.Subtype == "USD");

        Assert.NotNull(usd);
        Assert.Equal(754e6, usd.BaseValue, 1);
    }

    [Fact]
    public void Extract_MegatonnesSupplemental_YieldsMassKind()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "Carbon emissions fell to 12.5 megatonnes — wait, the unit here is tricky.");

        Quantity? megatonnes = quantities.FirstOrDefault(q =>
            q.Kind is UnitKind.Mass && q.Subtype == "mass-megatonne");

        Assert.NotNull(megatonnes);
        Assert.Equal(12.5, megatonnes.Value, 0.001);
    }

    [Fact]
    public void Extract_MillisecondAndSecond_YieldsDistinctDurationSubtypes()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "Server response time averaged 250 ms with peaks at 1.2 seconds.");

        Assert.Contains(quantities, q =>
            q.Kind is UnitKind.Duration && q.Subtype == "duration-millisecond" && Math.Abs(q.Value - 250) < 0.001);
        Assert.Contains(quantities, q =>
            q.Kind is UnitKind.Duration && q.Subtype == "duration-second" && Math.Abs(q.Value - 1.2) < 0.001);
    }

    [Fact]
    public void Extract_NumberWithoutUnit_YieldsPlainKind()
    {
        // Bare number with no adjacent unit signal → Plain. Matcher-layer Tier 3
        // wildcard handles cross-kind matching, NOT extractor inference.
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "The team grew to 47 people last quarter.");

        Quantity? plain = quantities.FirstOrDefault(q => Math.Abs(q.Value - 47) < 0.001);

        Assert.NotNull(plain);
        Assert.Equal(UnitKind.Plain, plain.Kind);
        Assert.Null(plain.Subtype);
    }

    [Fact]
    public void Extract_EmptyText_YieldsEmptyList()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(string.Empty);

        Assert.Empty(quantities);
    }

    [Fact]
    public void Extract_TextWithoutNumbers_YieldsEmptyList()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "AES is a publicly traded utility holding company.");

        Assert.Empty(quantities);
    }

    [Fact]
    public void Extract_FinancialMagnitudeShorthand_DoesNotCollideWithCldrDurationMinute()
    {
        // CLDR's narrow form for duration-minute is "m" (lowercase). Bare
        // financial "M" (million) must beat it under case-insensitive matching;
        // last-write-wins dedup ordered Units → Currencies → Magnitudes ensures
        // the Magnitude entry overrides the Duration entry.
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "The CEO sold 1.5M shares yesterday.");

        // The "1.5M" must yield a quantity with magnitude × 1e6, not a Duration.
        Quantity? shares = quantities.FirstOrDefault(q => Math.Abs(q.Value - 1.5) < 0.001);

        Assert.NotNull(shares);
        Assert.Equal(1.5e6, shares.BaseValue, 1);
        Assert.NotEqual(UnitKind.Duration, shares.Kind);
    }

    [Fact]
    public void Extract_BareFourDigitYearInRange_YieldsYearKind()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(
            "The report was published in 2024.");

        Quantity? year = quantities.FirstOrDefault(q => Math.Abs(q.Value - 2024) < 0.001);

        Assert.NotNull(year);
        Assert.Equal(UnitKind.Year, year.Kind);
        Assert.Null(year.Subtype);
    }

    [Theory]
    [InlineData(1900)]
    [InlineData(1995)]
    [InlineData(2000)]
    [InlineData(2024)]
    [InlineData(2100)]
    public void Extract_FourDigitNumberAtRangeBoundaries_YieldsYearKind(int yearValue)
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(yearValue.ToString());

        Quantity only = Assert.Single(quantities);
        Assert.Equal(UnitKind.Year, only.Kind);
        Assert.Equal(yearValue, only.Value);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(1899)]
    [InlineData(2101)]
    [InlineData(99999)]
    public void Extract_NumberOutsideYearRange_StaysPlain(int value)
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(value.ToString());

        Quantity only = Assert.Single(quantities);
        Assert.Equal(UnitKind.Plain, only.Kind);
    }

    [Fact]
    public void Extract_NonIntegerInYearRange_StaysPlain()
    {
        // 2024.5 isn't a year — fractional. Year detection requires integer.
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract("Reading 2024.5 was anomalous.");

        Quantity? hit = quantities.FirstOrDefault(q => Math.Abs(q.Value - 2024.5) < 0.001);

        Assert.NotNull(hit);
        Assert.Equal(UnitKind.Plain, hit.Kind);
    }

    [Fact]
    public void Extract_YearLikeNumberWithCurrencyPrefix_StaysCurrency()
    {
        // "$2024" is a price, not a year — Currency wins via prefix match.
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract("Hardware cost of $2024 per unit.");

        Quantity? price = quantities.FirstOrDefault(q =>
            q.Kind is UnitKind.Currency && q.Subtype == "USD");

        Assert.NotNull(price);
        Assert.Equal(2024, price.Value);
    }

    [Fact]
    public void Extract_YearLikeNumberWithMagnitude_StaysPlainWithMagnitude()
    {
        // "2024 million" reads as a quantity, not a year. Magnitude attachment
        // means the number isn't standing alone as a calendar reference.
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract("They counted 2024 million units.");

        Quantity? hit = quantities.FirstOrDefault(q => Math.Abs(q.Value - 2024) < 0.001);

        Assert.NotNull(hit);
        Assert.Equal(UnitKind.Plain, hit.Kind);
        Assert.Equal(1e6, hit.Multiplier);
        Assert.Equal(2.024e9, hit.BaseValue, 1);
    }

    [Fact]
    public void Extract_CommaGroupedYear_YieldsYearKind()
    {
        // "2,024" is the same number as 2024, just formatted with thousands
        // separator. Same Year classification.
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract("Published in 2,024.");

        Quantity only = Assert.Single(quantities);
        Assert.Equal(UnitKind.Year, only.Kind);
        Assert.Equal(2024, only.Value);
    }

    [Theory]
    [InlineData("Operating loss of (2,431) for the quarter.", -2431)]
    [InlineData("Impairment ($1,234) recognised in the period.", -1234)]
    [InlineData("Impairment $(1,234) recognised in the period.", -1234)]
    [InlineData("Movement (1,234) USD against the prior year.", -1234)]
    [InlineData("Net change of \u22122,431 on the statement.", -2431)]
    [InlineData("Net change of -2,431 on the statement.", -2431)]
    public void Extract_StatedNegative_CarriesTheSign(string text, double expected)
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(text);

        Quantity written = Assert.Single(quantities);
        Assert.Equal(expected, written.BaseValue, 0.001);
    }

    [Theory]
    [InlineData("Management fee - 150,000 charged annually.", 150000)]
    [InlineData("Return (net) 12.5 for the period.", 12.5)]
    [InlineData("Total 1,234 (unaudited) as reported.", 1234)]
    [InlineData("The (unaudited) 1,234 figure stands.", 1234)]
    [InlineData("Level-3 assets are disclosed separately.", 3)]
    [InlineData("Class A-1 notes were issued.", 1)]
    public void Extract_SignMarkerDetachedFromTheNumber_LeavesItPositive(string text, double expected)
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(text);

        Quantity written = Assert.Single(quantities);
        Assert.Equal(expected, written.BaseValue, 0.001);
    }

    [Theory]
    [InlineData("(1,234)")]
    [InlineData("($1,234)")]
    [InlineData("-1,234")]
    public void TryExtractSingle_StatedNegative_ReportsTheWholeValueAsItsSurface(string value)
    {
        Quantity? written = UnitExtractor.TryExtractSingle(value);

        Assert.NotNull(written);
        Assert.Equal(value, written.Surface);
        Assert.Equal(-1234, written.BaseValue, 0.001);
    }

    [Fact]
    public void Extract_ParenthesisedFourDigitInteger_IsNotReadAsAYear()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract("Retained earnings (2023) at year end.");

        Quantity only = Assert.Single(quantities);
        Assert.NotEqual(UnitKind.Year, only.Kind);
        Assert.Equal(-2023, only.Value);
    }

    [Theory]
    [InlineData("Loss of (1,234 for the year.", 1234)]
    [InlineData("Loss of 1,234) for the year.", 1234)]
    [InlineData("Total) 1,234 stated.", 1234)]
    [InlineData("Loss of ((1,234)) for the year.", -1234)]
    [InlineData("a-1,234 stated", 1234)]
    public void Extract_SignMarkersAtTheEdges_ReadAsWritten(string text, double expected)
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(text);

        Quantity written = Assert.Single(quantities);
        Assert.Equal(expected, written.BaseValue, 0.001);
    }

    [Theory]
    [InlineData("Highlights\n- 1,234 shares issued")]
    [InlineData("Highlights\r\n- 1,234 shares issued")]
    [InlineData("Summary:\n\n-  1,234 shares issued")]
    public void Extract_AListBullet_DoesNotNegateItsNumber(string text)
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract(text);

        Quantity written = Assert.Single(quantities);
        Assert.Equal(1234, written.BaseValue, 0.001);
    }

    [Fact]
    public void Extract_AValueThatIsNothingButADetachedMinus_StillReadsAsNegative()
    {
        Quantity? written = UnitExtractor.TryExtractSingle("- 1,234");

        Assert.NotNull(written);
        Assert.Equal(-1234, written.BaseValue, 0.001);
    }

    [Theory]
    [InlineData("The 5-10 year bucket was reweighted.")]
    [InlineData("The 5\u201310 year bucket was reweighted.")]
    [InlineData("FY 2024\u20132025 results were published.")]
    public void Extract_ARangeReadsTheSameWhicheverDashTypesetIt(string text)
    {
        Assert.Empty(UnitExtractor.Extract(text));
    }

    [Fact]
    public void Extract_ALabelDashBeforeAFigure_LeavesTheFigureStanding()
    {
        IReadOnlyList<Quantity> quantities = UnitExtractor.Extract("Revenue \u2014 1,234 for the year.");

        Quantity written = Assert.Single(quantities);
        Assert.Equal(1234, written.BaseValue, 0.001);
    }

    [Theory]
    [InlineData("Report as of 2024-01-15 for the quarter.")]
    [InlineData("The 5-10 year bucket was reweighted.")]
    public void Extract_ComponentsOfASeparatedToken_AreNotQuantities(string text)
    {
        Assert.Empty(UnitExtractor.Extract(text));
    }
}

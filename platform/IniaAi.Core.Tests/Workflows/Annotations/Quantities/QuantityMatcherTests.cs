using IniaAi.Core.Workflows.Annotations.Quantities;

namespace IniaAi.Core.Tests.Workflows.Annotations;

public class QuantityMatcherTests
{
    #region Equivalent — kind-aware comparison

    [Fact]
    public void Equivalent_SameKindSameSubtypeWithinTolerance_ReturnsTrue()
    {
        // Compute the within-tolerance pair from the configured tolerance so
        // the test doesn't break when the constant changes.
        double tol = QuantityMatcher.ToleranceByKind[UnitKind.Currency];
        Quantity a = MakeCurrency("USD", 100, 1);
        Quantity b = MakeCurrency("USD", 100 * (1 + (tol * 0.5)), 1);

        Assert.True(QuantityMatcher.Equivalent(a, b));
    }

    [Fact]
    public void Equivalent_SameKindSameSubtypeOutsideTolerance_ReturnsFalse()
    {
        // Currency tolerance is 0.5% — 100 vs 110 is 10%, way outside.
        Quantity a = MakeCurrency("USD", 100, 1);
        Quantity b = MakeCurrency("USD", 110, 1);

        Assert.False(QuantityMatcher.Equivalent(a, b));
    }

    [Fact]
    public void Equivalent_DifferentKinds_NeverMatch_DollarVsKelvin()
    {
        // The headline false-positive case: $300 billion vs 300 billion Kelvin.
        // The bare numeric values are equal but the kinds disagree → no match.
        Quantity dollars = MakeCurrency("USD", 300, 1e9);
        Quantity kelvin = MakeTyped(UnitKind.Temperature, "temperature-kelvin", 300, 1e9);

        Assert.False(QuantityMatcher.Equivalent(dollars, kelvin));
    }

    [Fact]
    public void Equivalent_DifferentSubtypesSameKind_ReturnsFalse()
    {
        // Currency/USD vs Currency/EUR — same Kind, different Subtype.
        // Numeric values equal, but USD ≠ EUR.
        Quantity usd = MakeCurrency("USD", 100, 1);
        Quantity eur = MakeCurrency("EUR", 100, 1);

        Assert.False(QuantityMatcher.Equivalent(usd, eur));
    }

    [Fact]
    public void Equivalent_PlainAndTyped_SameValue_ReturnsTrue()
    {
        // Plain wildcard: an AI rewriter dropped the "$" from "$60.9 billion"
        // and just left "60.9 billion". Plain matches Currency by value.
        Quantity plain = MakePlain(60.9, 1e9);
        Quantity typed = MakeCurrency("USD", 60.9, 1e9);

        Assert.True(QuantityMatcher.Equivalent(plain, typed));
        Assert.True(QuantityMatcher.Equivalent(typed, plain));
    }

    [Fact]
    public void Equivalent_BothPlain_SameValue_ReturnsTrue()
    {
        Quantity a = MakePlain(2024, 1);
        Quantity b = MakePlain(2024, 1);

        Assert.True(QuantityMatcher.Equivalent(a, b));
    }

    [Fact]
    public void Equivalent_BothPlain_DifferentValues_ReturnsFalse()
    {
        Quantity a = MakePlain(100, 1);
        Quantity b = MakePlain(110, 1);

        Assert.False(QuantityMatcher.Equivalent(a, b));
    }

    [Fact]
    public void Equivalent_DurationKind_ZeroTolerance()
    {
        // Duration tolerance is 0.0 — 250ms is NOT equivalent to 251ms.
        Quantity a = MakeTyped(UnitKind.Duration, "duration-millisecond", 250, 1);
        Quantity b = MakeTyped(UnitKind.Duration, "duration-millisecond", 251, 1);

        Assert.False(QuantityMatcher.Equivalent(a, b));
    }

    [Fact]
    public void Equivalent_DurationKind_ExactValue_ReturnsTrue()
    {
        Quantity a = MakeTyped(UnitKind.Duration, "duration-millisecond", 250, 1);
        Quantity b = MakeTyped(UnitKind.Duration, "duration-millisecond", 250, 1);

        Assert.True(QuantityMatcher.Equivalent(a, b));
    }

    [Fact]
    public void Equivalent_MassKind_LooserTolerance_AcceptsOnePercent()
    {
        // Mass tolerance is 1% — 100 vs 100.5 is 0.5%, within.
        Quantity a = MakeTyped(UnitKind.Mass, "mass-kilogram", 100, 1);
        Quantity b = MakeTyped(UnitKind.Mass, "mass-kilogram", 100.5, 1);

        Assert.True(QuantityMatcher.Equivalent(a, b));
    }

    [Fact]
    public void Equivalent_CurrencyKind_TighterThanZeroPointFivePercent_RejectsBalanceSheetAdjacents()
    {
        // Balance-sheet money: a 0.5% tolerance lets distinct figures like
        // "$184M" and "$184.5M" (different expense lines, same general size)
        // collide. The user's invariant for balance-sheet info is 0.05% —
        // tight enough that adjacent-but-distinct figures don't match while
        // still admitting near-rounding (e.g. $184B vs $183.95B at 0.027%).
        Quantity a = MakeCurrency("USD", 184, 1e6);
        Quantity b = MakeCurrency("USD", 184.5, 1e6);

        Assert.False(QuantityMatcher.Equivalent(a, b));
    }

    [Fact]
    public void Equivalent_CurrencyKind_AcceptsRephrasingInsideZeroPointZeroFivePercent()
    {
        // Regression guard: legitimate near-rounding rephrasings ("$184B" vs
        // "$183.95B" — 0.027% drift) still match under the tightened
        // Currency tolerance.
        Quantity a = MakeCurrency("USD", 184, 1e9);
        Quantity b = MakeCurrency("USD", 183.95, 1e9);

        Assert.True(QuantityMatcher.Equivalent(a, b));
    }

    [Fact]
    public void ToleranceByKind_CurrencyTighterThanMass()
    {
        Assert.True(
            QuantityMatcher.ToleranceByKind[UnitKind.Currency]
            < QuantityMatcher.ToleranceByKind[UnitKind.Mass]);
    }

    [Fact]
    public void Equivalent_DifferentYears_ReturnsFalse()
    {
        // Path 3 from findings.md: "2023" vs "2024" pre-Year were both Plain
        // and within 0.05% drift — wildcard tolerance let them collide. Year
        // kind has zero tolerance: distinct calendar years never match.
        Quantity y2023 = MakeTyped(UnitKind.Year, subtype: null, value: 2023, multiplier: 1);
        Quantity y2024 = MakeTyped(UnitKind.Year, subtype: null, value: 2024, multiplier: 1);

        Assert.False(QuantityMatcher.Equivalent(y2023, y2024));
    }

    [Fact]
    public void Equivalent_SameYear_ReturnsTrue()
    {
        Quantity a = MakeTyped(UnitKind.Year, subtype: null, value: 2024, multiplier: 1);
        Quantity b = MakeTyped(UnitKind.Year, subtype: null, value: 2024, multiplier: 1);

        Assert.True(QuantityMatcher.Equivalent(a, b));
    }

    [Fact]
    public void Equivalent_YearAndPlainSameValue_ReturnsTrue_ViaWildcard()
    {
        // Recovery case: extractor missed Year detection on one side (e.g.
        // because its surface was non-canonical). Plain wildcard still pairs
        // them by value so the citation isn't lost.
        Quantity year = MakeTyped(UnitKind.Year, subtype: null, value: 2024, multiplier: 1);
        Quantity plain = MakePlain(2024, 1);

        Assert.True(QuantityMatcher.Equivalent(year, plain));
        Assert.True(QuantityMatcher.Equivalent(plain, year));
    }

    #endregion

    #region FindBestMatch — Tier 2 / Tier 3 over quantity haystack

    [Fact]
    public void FindBestMatch_NoCandidates_ReturnsNull()
    {
        Quantity needle = MakeCurrency("USD", 100, 1);
        IReadOnlyList<Quantity> haystack = [];

        QuantityMatch? result = QuantityMatcher.FindBestMatch(needle, haystack);

        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatch_TypedAgainstSameTypedInHaystack_ReturnsTier2()
    {
        // Pair derived from the configured tolerance so the test doesn't
        // break when the Currency tolerance is retuned.
        double tol = QuantityMatcher.ToleranceByKind[UnitKind.Currency];
        Quantity needle = MakeCurrency("USD", 100, 1);
        IReadOnlyList<Quantity> haystack =
        [
            MakeCurrency("USD", 100 * (1 + (tol * 0.5)), 1, start: 10, end: 20),
        ];

        QuantityMatch? result = QuantityMatcher.FindBestMatch(needle, haystack);

        Assert.NotNull(result);
        Assert.Equal(MatchTier.TypedQuantity, result.Tier);
        Assert.Equal("USD", result.Quantity.Subtype);
    }

    [Fact]
    public void FindBestMatch_PlainNeedleAgainstTypedHaystack_ReturnsTier3()
    {
        // Needle is bare value (Plain), haystack has a typed Currency.
        // Match by value only, classified as Tier 3 wildcard.
        Quantity needle = MakePlain(60.9, 1e9);
        IReadOnlyList<Quantity> haystack =
        [
            MakeCurrency("USD", 60.9, 1e9, start: 10, end: 22),
        ];

        QuantityMatch? result = QuantityMatcher.FindBestMatch(needle, haystack);

        Assert.NotNull(result);
        Assert.Equal(MatchTier.PlainWildcard, result.Tier);
    }

    [Fact]
    public void FindBestMatch_PrefersTier2OverTier3_WhenBothAvailable()
    {
        // Needle: Currency USD 100. Haystack has both a typed match (USD 100)
        // and a plain match (100). Tier 2 wins.
        Quantity needle = MakeCurrency("USD", 100, 1);
        Quantity tier2 = MakeCurrency("USD", 100, 1, start: 0, end: 4);
        Quantity tier3 = MakePlain(100, 1, start: 50, end: 53);
        IReadOnlyList<Quantity> haystack = [tier3, tier2];

        QuantityMatch? result = QuantityMatcher.FindBestMatch(needle, haystack);

        Assert.NotNull(result);
        Assert.Equal(MatchTier.TypedQuantity, result.Tier);
        Assert.Same(tier2, result.Quantity);
    }

    [Fact]
    public void FindBestMatch_DifferentKindsInHaystack_ReturnsNull()
    {
        // Needle: Currency USD 300B. Haystack: Temperature Kelvin 300B.
        // Different kinds; matcher must return null (no false positive).
        Quantity needle = MakeCurrency("USD", 300, 1e9);
        IReadOnlyList<Quantity> haystack =
        [
            MakeTyped(UnitKind.Temperature, "temperature-kelvin", 300, 1e9, start: 0, end: 13),
        ];

        QuantityMatch? result = QuantityMatcher.FindBestMatch(needle, haystack);

        Assert.Null(result);
    }

    [Fact]
    public void FindBestMatch_SkipsClaimedSpans()
    {
        Quantity needle = MakeCurrency("USD", 100, 1);
        Quantity claimed = MakeCurrency("USD", 100, 1, start: 0, end: 4);
        Quantity unclaimed = MakeCurrency("USD", 100, 1, start: 50, end: 54);
        IReadOnlyList<Quantity> haystack = [claimed, unclaimed];
        var claims = new HashSet<(int, int)> { (0, 4) };

        QuantityMatch? result = QuantityMatcher.FindBestMatch(needle, haystack, claims);

        Assert.NotNull(result);
        Assert.Same(unclaimed, result.Quantity);
    }

    [Fact]
    public void FindBestMatch_TieBreaksByCloserBaseValue()
    {
        // Two haystack candidates within tolerance. The closer value wins.
        // Both deltas are derived from the configured Currency tolerance so
        // the test stays valid as the constant is retuned.
        double tol = QuantityMatcher.ToleranceByKind[UnitKind.Currency];
        Quantity needle = MakeCurrency("USD", 100, 1);
        Quantity farther = MakeCurrency("USD", 100 * (1 + (tol * 0.8)), 1, start: 0, end: 6);
        Quantity closer = MakeCurrency("USD", 100 * (1 + (tol * 0.2)), 1, start: 50, end: 56);
        IReadOnlyList<Quantity> haystack = [farther, closer];

        QuantityMatch? result = QuantityMatcher.FindBestMatch(needle, haystack);

        Assert.NotNull(result);
        Assert.Same(closer, result.Quantity);
    }

    #endregion

    #region Equivalent — cross-subtype conversion within a dimension

    [Fact]
    public void Equivalent_MassAcrossSubtypes_SameFactInDifferentUnits_ReturnsTrue()
    {
        // "1.2 Mt" and "1200 kt" state the same mass; before conversion the
        // subtype gate made this pair unmatchable by construction.
        Quantity megatonnes = MakeTyped(UnitKind.Mass, "mass-megatonne", 1.2, 1);
        Quantity kilotonnes = MakeTyped(UnitKind.Mass, "mass-kilotonne", 1200, 1);

        Assert.True(QuantityMatcher.Equivalent(megatonnes, kilotonnes));
        Assert.True(QuantityMatcher.Equivalent(kilotonnes, megatonnes));
    }

    [Fact]
    public void Equivalent_MassAcrossSubtypes_DifferentValues_ReturnsFalse()
    {
        Quantity megatonnes = MakeTyped(UnitKind.Mass, "mass-megatonne", 1.2, 1);
        Quantity kilotonnes = MakeTyped(UnitKind.Mass, "mass-kilotonne", 1300, 1);

        Assert.False(QuantityMatcher.Equivalent(megatonnes, kilotonnes));
    }

    [Fact]
    public void Equivalent_MegatonneVsMillionTonnes_ReturnsTrue()
    {
        // "1.2 Mt" vs "1.2 million tonnes" — same fact, split by subtype today
        // because the magnitude word lands in Multiplier, not in the unit.
        Quantity megatonnes = MakeTyped(UnitKind.Mass, "mass-megatonne", 1.2, 1);
        Quantity millionTonnes = MakeTyped(UnitKind.Mass, "mass-tonne", 1.2, 1e6);

        Assert.True(QuantityMatcher.Equivalent(megatonnes, millionTonnes));
    }

    [Fact]
    public void Equivalent_LengthAcrossSubtypes_KilometerVsMeter_ReturnsTrue()
    {
        Quantity kilometers = MakeTyped(UnitKind.Length, "length-kilometer", 1.2, 1);
        Quantity meters = MakeTyped(UnitKind.Length, "length-meter", 1200, 1);

        Assert.True(QuantityMatcher.Equivalent(kilometers, meters));
    }

    [Fact]
    public void Equivalent_PercentVsBasisPoints_ReturnsTrue()
    {
        Quantity percent = MakeTyped(UnitKind.Concentration, "concentr-percent", 1.25, 1);
        Quantity basisPoints = MakeTyped(UnitKind.Concentration, "concentr-basis-point", 125, 1);

        Assert.True(QuantityMatcher.Equivalent(percent, basisPoints));
    }

    [Fact]
    public void Equivalent_SameKindDifferentDimension_NeverMatch()
    {
        // Kind is CLDR's display category, not a dimension: percent is a
        // portion, mg/dL is mass-per-volume. No conversion exists between
        // them, so equal numbers must not pair up.
        Quantity percent = MakeTyped(UnitKind.Concentration, "concentr-percent", 5, 1);
        Quantity glucose = MakeTyped(
            UnitKind.Concentration, "concentr-milligram-ofglucose-per-deciliter", 5, 1);

        Assert.False(QuantityMatcher.Equivalent(percent, glucose));
    }

    [Fact]
    public void Equivalent_DigitalAcrossSubtypes_ByteVsBit_ReturnsTrue()
    {
        Quantity bytes = MakeTyped(UnitKind.Digital, "digital-byte", 1, 1);
        Quantity bits = MakeTyped(UnitKind.Digital, "digital-bit", 8, 1);
        Quantity nineBits = MakeTyped(UnitKind.Digital, "digital-bit", 9, 1);

        Assert.True(QuantityMatcher.Equivalent(bytes, bits));
        Assert.False(QuantityMatcher.Equivalent(bytes, nineBits));
    }

    [Fact]
    public void Equivalent_TwelveMonthsVsOneYear_ExactUnderZeroTolerance()
    {
        // Duration tolerance is 0.0 and month = year/12 is a non-terminating
        // division — the comparison must cross-multiply so this holds exactly.
        Quantity months = MakeTyped(UnitKind.Duration, "duration-month", 12, 1);
        Quantity year = MakeTyped(UnitKind.Duration, "duration-year", 1, 1);

        Assert.True(QuantityMatcher.Equivalent(months, year));
    }

    [Fact]
    public void Equivalent_DaysVsMonths_NeverMatch()
    {
        // CLDR splits clock durations (base second) from calendar durations
        // (base year): converting 90 days into months needs context the
        // extractor doesn't have, so the pair deliberately stays unmatched.
        Quantity days = MakeTyped(UnitKind.Duration, "duration-day", 90, 1);
        Quantity months = MakeTyped(UnitKind.Duration, "duration-month", 3, 1);

        Assert.False(QuantityMatcher.Equivalent(days, months));
    }

    [Fact]
    public void Equivalent_TemperatureAffine_FahrenheitVsCelsius_ReturnsTrue()
    {
        Quantity fahrenheit = MakeTyped(UnitKind.Temperature, "temperature-fahrenheit", 32, 1);
        Quantity celsius = MakeTyped(UnitKind.Temperature, "temperature-celsius", 0, 1);

        Assert.True(QuantityMatcher.Equivalent(fahrenheit, celsius));
    }

    [Fact]
    public void Equivalent_TemperatureGeneric_HasNoConversion_ReturnsFalse()
    {
        // The bare degree sign has no defined conversion; a subtype missing
        // from the table is loudly non-convertible, never a silent ×1.
        Quantity generic = MakeTyped(UnitKind.Temperature, "temperature-generic", 0, 1);
        Quantity celsius = MakeTyped(UnitKind.Temperature, "temperature-celsius", 0, 1);

        Assert.False(QuantityMatcher.Equivalent(generic, celsius));
    }

    [Fact]
    public void Equivalent_CentsVsDollars_ReturnsTrue()
    {
        // "50 cents" vs "$0.50" — dividends and EPS are routinely stated in
        // cents while extractions restate them in dollars.
        Quantity cents = MakeCurrency("USD-cent", 50, 1);
        Quantity dollars = MakeCurrency("USD", 0.5, 1);

        Assert.True(QuantityMatcher.Equivalent(cents, dollars));
    }

    [Fact]
    public void Equivalent_ConsumptionReciprocalPair_DeliberatelyNotConverted()
    {
        // 5 l/100km ≈ 47 mpg describe the same efficiency, but the two units
        // live on reciprocal dimensions. Inverting a value is not a linear
        // conversion, so the pair is deliberately excluded.
        Quantity milesPerGallon = MakeTyped(
            UnitKind.Consumption, "consumption-mile-per-gallon", 47.04, 1);
        Quantity litersPerHundredKm = MakeTyped(
            UnitKind.Consumption, "consumption-liter-per-100-kilometer", 5, 1);

        Assert.False(QuantityMatcher.Equivalent(milesPerGallon, litersPerHundredKm));
    }

    [Fact]
    public void Equivalent_GallonsAgainstLiters_ConvertOnTheLegalDefinition()
    {
        // One US gallon is exactly 3.785411784 liters. This anchors the whole
        // volume family: the CLDR expression grammar reads a/b*c*d as
        // a/(b·c·d), and a left-to-right parse once shipped a table where a
        // gallon equalled a pinch.
        Quantity gallon = MakeTyped(UnitKind.Volume, "volume-gallon", 1, 1);
        Quantity liters = MakeTyped(UnitKind.Volume, "volume-liter", 3.785411784, 1);
        Quantity pinch = MakeTyped(UnitKind.Volume, "volume-pinch", 1, 1);

        Assert.True(QuantityMatcher.Equivalent(gallon, liters));
        Assert.False(QuantityMatcher.Equivalent(gallon, pinch));
    }

    [Fact]
    public void Equivalent_PlainWildcard_ComparesAsWritten_NotCanonical()
    {
        // Tier-3 recovery: a bare "90" in a table keeps matching "90 days"
        // exactly as before conversion existed — and the canonical
        // seconds value must not leak into the wildcard comparison.
        Quantity days = MakeTyped(UnitKind.Duration, "duration-day", 90, 1);

        Assert.True(QuantityMatcher.Equivalent(MakePlain(90, 1), days));
        Assert.False(QuantityMatcher.Equivalent(MakePlain(7_776_000, 1), days));
    }

    [Fact]
    public void Equivalent_TemperatureAffine_ToleranceReadsOnTheStatedScale_NotKelvin()
    {
        // 32 °F is 0 °C, not 2 °C. On the Kelvin scale the two sit 0.7% apart,
        // inside Temperature's relative tolerance — the offset of ~273 would
        // turn a relative band into a ±2.7-degree absolute one, so the
        // tolerance must read on the compared unit's own scale.
        Quantity fahrenheit = MakeTyped(UnitKind.Temperature, "temperature-fahrenheit", 32, 1);
        Quantity celsius = MakeTyped(UnitKind.Temperature, "temperature-celsius", 2, 1);

        Assert.False(QuantityMatcher.Equivalent(fahrenheit, celsius));
        Assert.False(QuantityMatcher.Equivalent(celsius, fahrenheit));

        Quantity boilingF = MakeTyped(UnitKind.Temperature, "temperature-fahrenheit", 212, 1);
        Quantity boilingC = MakeTyped(UnitKind.Temperature, "temperature-celsius", 100, 1);

        Assert.True(QuantityMatcher.Equivalent(boilingF, boilingC));
    }

    [Fact]
    public void Equivalent_FractionalYearsVsMonths_ExactUnderZeroTolerance()
    {
        // 1.1 × 12 in double is 13.200000000000001; the zero-tolerance
        // comparison must cross-multiply in decimal or genuinely equal
        // pairs read as different.
        Quantity years = MakeTyped(UnitKind.Duration, "duration-year", 1.1, 1);
        Quantity months = MakeTyped(UnitKind.Duration, "duration-month", 13.2, 1);

        Assert.True(QuantityMatcher.Equivalent(years, months));
        Assert.True(QuantityMatcher.Equivalent(months, years));
    }

    [Fact]
    public void Equivalent_PercentagePointsVsPercent_NeverMatch()
    {
        // A change of 5 pp and a level of 5% are different claims — the
        // supplemental lexicon split the subtypes precisely to keep them apart.
        Quantity points = MakeTyped(UnitKind.Concentration, "concentr-percentage-point", 5, 1);
        Quantity percent = MakeTyped(UnitKind.Concentration, "concentr-percent", 5, 1);
        Quantity basisPoints = MakeTyped(UnitKind.Concentration, "concentr-basis-point", 500, 1);

        Assert.False(QuantityMatcher.Equivalent(points, percent));
        Assert.False(QuantityMatcher.Equivalent(points, basisPoints));
    }

    [Fact]
    public void Equivalent_KaratVsPercent_NeverMatch()
    {
        Quantity karat = MakeTyped(UnitKind.Concentration, "concentr-karat", 18, 1);
        Quantity percent = MakeTyped(UnitKind.Concentration, "concentr-percent", 75, 1);

        Assert.False(QuantityMatcher.Equivalent(karat, percent));
    }

    [Fact]
    public void Comparable_SubtypesSharingADimension_True()
    {
        Quantity kilometers = MakeTyped(UnitKind.Length, "length-kilometer", 3, 1);
        Quantity miles = MakeTyped(UnitKind.Length, "length-mile", 5, 1);

        Assert.True(QuantityMatcher.Comparable(kilometers, miles));
    }

    [Fact]
    public void Comparable_SubtypesOfOneKindWithoutASharedDimension_False()
    {
        Quantity karat = MakeTyped(UnitKind.Concentration, "concentr-karat", 22, 1);
        Quantity percent = MakeTyped(UnitKind.Concentration, "concentr-percent", 91.7, 1);

        Assert.False(QuantityMatcher.Comparable(karat, percent));
    }

    [Fact]
    public void Comparable_CrossKindOrPlain_False()
    {
        Quantity years = MakeTyped(UnitKind.Duration, "duration-year", 3, 1);
        Quantity percent = MakeTyped(UnitKind.Concentration, "concentr-percent", 3, 1);
        Quantity plain = new(UnitKind.Plain, null, 3, 1, 3, "3", 0, 1);

        Assert.False(QuantityMatcher.Comparable(years, percent));
        Assert.False(QuantityMatcher.Comparable(plain, plain));
    }

    [Fact]
    public void Equivalent_CollapsedCldrDimensions_StayDistinct()
    {
        // CLDR's dimensional algebra reduces lumen and candela both to
        // candela (the steradian is dropped), dot to pixel, and kWh/100km to
        // newtons. Each pair states a different physical claim.
        Assert.False(QuantityMatcher.Equivalent(
            MakeTyped(UnitKind.Light, "light-lumen", 800, 1),
            MakeTyped(UnitKind.Light, "light-candela", 800, 1)));
        Assert.False(QuantityMatcher.Equivalent(
            MakeTyped(UnitKind.Graphics, "graphics-dot", 300, 1),
            MakeTyped(UnitKind.Graphics, "graphics-pixel", 300, 1)));
        Assert.False(QuantityMatcher.Equivalent(
            MakeTyped(UnitKind.Force, "force-newton", 36, 1),
            MakeTyped(UnitKind.Force, "force-kilowatt-hour-per-100-kilometer", 1, 1)));
    }

    [Fact]
    public void Equivalent_BeaufortVsMetersPerSecond_NeverMatch()
    {
        // CLDR marks Beaufort special=: the scale is non-linear (Beaufort 8
        // is 17.2–20.7 m/s), so no linear factor can relate it to any speed.
        Quantity beaufort = MakeTyped(UnitKind.Speed, "speed-beaufort", 8, 1);
        Quantity metersPerSecond = MakeTyped(UnitKind.Speed, "speed-meter-per-second", 8, 1);

        Assert.False(QuantityMatcher.Equivalent(beaufort, metersPerSecond));
    }

    [Fact]
    public void RankingDelta_MeasuresTypedPairsOnTheCanonicalScale()
    {
        Quantity megatonnes = MakeTyped(UnitKind.Mass, "mass-megatonne", 1.2, 1);
        Quantity kilotonnes = MakeTyped(UnitKind.Mass, "mass-kilotonne", 1200, 1);

        Assert.Equal(0.0, QuantityMatcher.RankingDelta(megatonnes, kilotonnes));
        Assert.Equal(1198.8, QuantityMatcher.RankingDelta(MakePlain(1.2, 1), kilotonnes));
    }

    [Fact]
    public void FindBestMatch_TiedOnDelta_PrefersTheLiteralSubtypeSpan()
    {
        // "emissions of 1200 kt, i.e. 1.2 Mt in total" — both spans are
        // exactly equal after conversion, but the citation must highlight the
        // span written in the extraction's own unit.
        Quantity needle = MakeTyped(UnitKind.Mass, "mass-megatonne", 1.2, 1);
        Quantity kilotonnes = MakeTyped(
            UnitKind.Mass, "mass-kilotonne", 1200, 1, start: 13, end: 20);
        Quantity megatonnes = MakeTyped(
            UnitKind.Mass, "mass-megatonne", 1.2, 1, start: 27, end: 33);
        IReadOnlyList<Quantity> haystack = [kilotonnes, megatonnes];

        QuantityMatch? result = QuantityMatcher.FindBestMatch(needle, haystack);

        Assert.NotNull(result);
        Assert.Same(megatonnes, result.Quantity);
    }

    [Fact]
    public void FindBestMatch_CrossSubtypeEquivalent_LandsInTypedTier()
    {
        Quantity needle = MakeTyped(UnitKind.Mass, "mass-megatonne", 1.2, 1);
        Quantity plain = MakePlain(1.2, 1, start: 0, end: 3);
        Quantity kilotonnes = MakeTyped(
            UnitKind.Mass, "mass-kilotonne", 1200, 1, start: 10, end: 17);
        IReadOnlyList<Quantity> haystack = [plain, kilotonnes];

        QuantityMatch? result = QuantityMatcher.FindBestMatch(needle, haystack);

        Assert.NotNull(result);
        Assert.Equal(MatchTier.TypedQuantity, result.Tier);
        Assert.Same(kilotonnes, result.Quantity);
    }

    #endregion

    #region helpers

    private static Quantity MakeCurrency(string isoCode, double value, double multiplier, int start = 0, int end = 0)
    {
        return new Quantity(
            Kind: UnitKind.Currency,
            Subtype: isoCode,
            Value: value,
            Multiplier: multiplier,
            BaseValue: value * multiplier,
            Surface: $"{isoCode} {value}",
            Start: start,
            End: end);
    }

    private static Quantity MakeTyped(UnitKind kind, string? subtype, double value, double multiplier, int start = 0, int end = 0)
    {
        return new Quantity(
            Kind: kind,
            Subtype: subtype,
            Value: value,
            Multiplier: multiplier,
            BaseValue: value * multiplier,
            Surface: $"{value} {subtype}",
            Start: start,
            End: end);
    }

    private static Quantity MakePlain(double value, double multiplier, int start = 0, int end = 0)
    {
        return new Quantity(
            Kind: UnitKind.Plain,
            Subtype: null,
            Value: value,
            Multiplier: multiplier,
            BaseValue: value * multiplier,
            Surface: value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Start: start,
            End: end);
    }

    #endregion

    #region Accounting sign

    [Fact]
    public void ContainsEquivalent_AParenthesisedFigure_DoesNotGroundItsPositiveTwin()
    {
        Assert.False(QuantityMatcher.ContainsEquivalent("Operating loss of (2,431) for the quarter.", "2,431"));
    }

    [Fact]
    public void ContainsEquivalent_AParenthesisedFigure_GroundsTheSameNegative()
    {
        Assert.True(QuantityMatcher.ContainsEquivalent("Operating loss of (2,431) for the quarter.", "(2,431)"));
    }

    #endregion
}

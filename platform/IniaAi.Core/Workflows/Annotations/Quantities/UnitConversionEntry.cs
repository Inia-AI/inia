namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// One subtype's conversion to its dimension's base unit, as exact integer
/// rationals: canonical = value × FactorNumerator / FactorDenominator, plus
/// the offset for affine temperature units. Two subtypes convert only when
/// they share a dimension. Equality comparison must cross-multiply via
/// <see cref="CrossMultiplied"/> rather than divide — month is year/12, a
/// non-terminating division, and Duration's zero tolerance needs 12 months to
/// equal 1 year exactly. <see cref="CanonicalOf"/> divides deliberately and is
/// for the affine path and ranking distances only, never for equality.
/// <see cref="CurrencyCode"/> names the ISO family a currency unit belongs to
/// (cents carry USD); it is null for every non-currency unit.
/// </summary>
internal sealed record UnitConversionEntry(
    string Dimension,
    decimal FactorNumerator,
    decimal FactorDenominator,
    decimal OffsetNumerator,
    decimal OffsetDenominator,
    string? CurrencyCode)
{
    private const double LARGEST_DOUBLE_A_DECIMAL_HOLDS = 7.9e28;

    public bool HasOffset => OffsetNumerator != 0m;

    public bool IsCurrency => CurrencyCode is not null;

    public bool SharesDimensionWith(UnitConversionEntry other)
    {
        return Dimension == other.Dimension;
    }

    public double CanonicalOf(double value)
    {
        return (value * (double)FactorNumerator / (double)FactorDenominator)
            + ((double)OffsetNumerator / (double)OffsetDenominator);
    }

    /// <summary>
    /// Inverse of <see cref="CanonicalOf"/>: expresses a canonical value in
    /// this unit's own scale, so an affine pair can be compared where a
    /// relative tolerance keeps its stated meaning — on the Kelvin scale the
    /// ~273 offset would turn 1% into a ±2.7-degree absolute band.
    /// </summary>
    public double ValueFrom(double canonical)
    {
        return (canonical - ((double)OffsetNumerator / (double)OffsetDenominator))
            * (double)FactorDenominator / (double)FactorNumerator;
    }

    public static (double Left, double Right) CrossMultiplied(
        double leftValue,
        UnitConversionEntry left,
        double rightValue,
        UnitConversionEntry right)
    {
        return (
            leftValue * (double)left.FactorNumerator * (double)right.FactorDenominator,
            rightValue * (double)right.FactorNumerator * (double)left.FactorDenominator);
    }

    /// <summary>
    /// The double form of <see cref="CrossMultiplied"/> is only exact for
    /// dyadic-rational values — 1.1 × 12 in double is 13.200000000000001, so
    /// zero-tolerance kinds must cross-multiply in decimal. False when either
    /// value or product leaves the decimal range; callers then fall back to
    /// the double comparison, where exactness at that magnitude is moot.
    /// </summary>
    public static bool TryCrossMultipliedExact(
        double leftValue,
        UnitConversionEntry left,
        double rightValue,
        UnitConversionEntry right,
        out decimal crossLeft,
        out decimal crossRight)
    {
        crossLeft = 0m;
        crossRight = 0m;

        if (!FitsInDecimal(leftValue) || !FitsInDecimal(rightValue))
        {
            return false;
        }

        return TryMultiply((decimal)leftValue, left.FactorNumerator, out decimal leftScaled)
            && TryMultiply(leftScaled, right.FactorDenominator, out crossLeft)
            && TryMultiply((decimal)rightValue, right.FactorNumerator, out decimal rightScaled)
            && TryMultiply(rightScaled, left.FactorDenominator, out crossRight);
    }

    /// <summary>
    /// Affine analogue of <see cref="TryCrossMultipliedExact"/>: both sides of
    /// "left expressed in right's unit vs right" are scaled by the same
    /// positive product of denominators, which leaves the relative tolerance's
    /// meaning on right's own scale but makes the arithmetic exact — dividing
    /// through the offsets leaves 32 °F a residue away from 0 °C, and against
    /// a target of zero any residue at all fails a relative tolerance.
    /// </summary>
    public static bool TryAffineCross(
        double leftValue,
        UnitConversionEntry left,
        double rightValue,
        UnitConversionEntry right,
        out decimal crossLeft,
        out decimal crossRight)
    {
        crossLeft = 0m;
        crossRight = 0m;

        if (!FitsInDecimal(leftValue) || !FitsInDecimal(rightValue))
        {
            return false;
        }

        if (!TryMultiply((decimal)leftValue, left.FactorNumerator, out decimal value)
            || !TryMultiply(value, left.OffsetDenominator, out value)
            || !TryMultiply(value, right.OffsetDenominator, out value)
            || !TryMultiply(left.OffsetNumerator, left.FactorDenominator, out decimal leftOffset)
            || !TryMultiply(leftOffset, right.OffsetDenominator, out leftOffset)
            || !TryMultiply(right.OffsetNumerator, left.FactorDenominator, out decimal rightOffset)
            || !TryMultiply(rightOffset, left.OffsetDenominator, out rightOffset))
        {
            return false;
        }

        decimal shift = leftOffset - rightOffset;

        if (value > 0m == shift > 0m && Math.Abs(value) > decimal.MaxValue - Math.Abs(shift))
        {
            return false;
        }

        if (!TryMultiply(value + shift, right.FactorDenominator, out crossLeft)
            || !TryMultiply((decimal)rightValue, left.FactorDenominator, out decimal scaled)
            || !TryMultiply(scaled, left.OffsetDenominator, out scaled)
            || !TryMultiply(scaled, right.OffsetDenominator, out scaled)
            || !TryMultiply(scaled, right.FactorNumerator, out crossRight))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Multiplying by one-or-less cannot overflow, and guarding it anyway
    /// would overflow the guard itself: <c>decimal.MaxValue / 0.01m</c> throws
    /// before the comparison runs. Every overflow-guarded decimal
    /// multiplication in conversion and validation arithmetic goes through
    /// here so that trap is handled exactly once.
    /// </summary>
    public static bool TryMultiply(decimal value, decimal by, out decimal result)
    {
        result = 0m;

        if (by > 1m && Math.Abs(value) > decimal.MaxValue / by)
        {
            return false;
        }

        result = value * by;

        return true;
    }

    private static bool FitsInDecimal(double value)
    {
        return double.IsFinite(value) && Math.Abs(value) < LARGEST_DOUBLE_A_DECIMAL_HOLDS;
    }
}

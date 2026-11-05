using System.Diagnostics.CodeAnalysis;

namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// Kind-aware comparison for <see cref="Quantity"/> values. The prior
/// flat 1% numeric-only matcher produced false positives across kinds
/// (<c>$300 billion</c> matched <c>300 billion Kelvin</c> on the bare
/// numeric value); per-kind tolerance plus same-Kind+Subtype gating
/// rejects those.
///
/// Equivalence rules:
/// <list type="bullet">
///   <item>Both sides typed, same Kind+Subtype: compare BaseValue within
///         the kind's tolerance.</item>
///   <item>Both sides typed, same Kind, different Subtype: convert through
///         <see cref="UnitConversionTable"/> when the subtypes share a
///         conversion dimension; otherwise never match.</item>
///   <item>Both sides typed, different Kind: never match.</item>
///   <item>Either side Plain: compare BaseValue as written within
///         <see cref="PLAIN_WILDCARD_TOLERANCE"/>, ignore kind — canonical
///         values never leak into the wildcard tier.</item>
/// </list>
///
/// Tier 1 substring matching is handled by callers against raw text — this
/// matcher operates on already-extracted <see cref="Quantity"/> records.
/// </summary>
public static class QuantityMatcher
{
    public const double DEFAULT_TYPED_TOLERANCE = 0.01;
    public const double PLAIN_WILDCARD_TOLERANCE = 0.0005;

    /// <summary>
    /// Per-kind tolerance for typed matches. Financial-data kinds (Currency,
    /// Concentration) are tightest at 0.05% — balance-sheet figures like
    /// "$184M" and "$184.5M" are distinct facts and "85%" vs "85.05%" can
    /// describe completely different ratios; same-kind near-collisions in
    /// the 0.05–0.5% band were producing false-positive citations. Physical
    /// magnitudes use the 1% default (rounding common). Duration and Year
    /// are zero — rounding is not acceptable in time / calendar claims.
    /// Plain wildcard (cross-kind recovery) tracks the financial tolerance.
    /// </summary>
    /// <summary>Floating-point noise only: 55.1 × 10⁶ read from "$55.1m" and from "$55.1 million".</summary>
    public const double SAME_AMOUNT_RELATIVE_ERROR = 1e-9;

    public static readonly IReadOnlyDictionary<UnitKind, double> ToleranceByKind =
        new Dictionary<UnitKind, double>
        {
            [UnitKind.Currency] = 0.0005,
            [UnitKind.Concentration] = 0.001,
            [UnitKind.Duration] = 0.0,
            [UnitKind.Year] = 0.0,
            [UnitKind.Acceleration] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Angle] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Area] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Consumption] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Digital] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Electric] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Energy] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Force] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Frequency] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Graphics] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Length] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Light] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Mass] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Power] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Pressure] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Speed] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Temperature] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Torque] = DEFAULT_TYPED_TOLERANCE,
            [UnitKind.Volume] = DEFAULT_TYPED_TOLERANCE,
        };

    /// <summary>
    /// Returns whether two quantities are equivalent under the kind-aware
    /// tolerance rules, or under <paramref name="tolerance"/> for typed
    /// quantities when one is given.
    /// </summary>
    public static bool Equivalent(Quantity a, Quantity b, double? tolerance = null)
    {
        if (a.SignStatedInWords || b.SignStatedInWords)
        {
            (a, b) = (a.Magnitude(), b.Magnitude());
        }

        if (a.Kind is UnitKind.Plain || b.Kind is UnitKind.Plain)
        {
            return WithinTolerance(a.BaseValue, b.BaseValue, PLAIN_WILDCARD_TOLERANCE);
        }

        if (a.Kind != b.Kind)
        {
            return false;
        }

        if (a.Subtype != b.Subtype)
        {
            return EquivalentAcrossSubtypes(a, b, tolerance ?? ToleranceFor(a.Kind));
        }

        return WithinTolerance(a.BaseValue, b.BaseValue, tolerance ?? ToleranceFor(a.Kind));
    }

    /// <summary>
    /// Whether two quantities measure the same thing: same kind, and either the same subtype
    /// or subtypes the conversion table places in one dimension. When this holds, a rejection
    /// from <see cref="Equivalent"/> means the values disagree; when it does not, the pair is
    /// incommensurable and no value comparison was possible at all. Plain quantities are
    /// never comparable here — the wildcard tier matches values, not units.
    /// </summary>
    public static bool Comparable(Quantity a, Quantity b)
    {
        if (a.Kind != b.Kind || a.Kind is UnitKind.Plain)
        {
            return false;
        }

        return a.Subtype == b.Subtype || TryGetSharedDimensionEntries(a, b, out _, out _);
    }

    /// <summary>
    /// Same-kind pairs stated in different subtypes compare through the CLDR
    /// conversion table. Both sides must share a conversion dimension — kind
    /// is a display category, so percent and mg/dL sit in one kind but never
    /// meet — and a subtype missing from the table never matches, so an
    /// unknown unit cannot be compared on a silently wrong scale. Zero-
    /// tolerance kinds cross-multiply in decimal so 13.2 months equals
    /// 1.1 years exactly; affine temperature pairs convert one side into the
    /// other's own scale, where the relative tolerance keeps its stated
    /// meaning instead of widening by the ~273 Kelvin offset.
    /// </summary>
    private static bool EquivalentAcrossSubtypes(Quantity a, Quantity b, double tolerance)
    {
        if (!TryGetSharedDimensionEntries(a, b, out UnitConversionEntry? left, out UnitConversionEntry? right))
        {
            return false;
        }

        if (left.HasOffset || right.HasOffset)
        {
            if (UnitConversionEntry.TryAffineCross(
                a.BaseValue, left, b.BaseValue, right, out decimal affineLeft, out decimal affineRight))
            {
                return WithinTolerance((double)affineLeft, (double)affineRight, tolerance);
            }

            return WithinTolerance(right.ValueFrom(left.CanonicalOf(a.BaseValue)), b.BaseValue, tolerance);
        }

        if (tolerance <= 0.0
            && UnitConversionEntry.TryCrossMultipliedExact(
                a.BaseValue, left, b.BaseValue, right, out decimal exactLeft, out decimal exactRight))
        {
            return exactLeft == exactRight;
        }

        (double crossLeft, double crossRight) = UnitConversionEntry.CrossMultiplied(
            a.BaseValue, left, b.BaseValue, right);

        return WithinTolerance(crossLeft, crossRight, tolerance);
    }

    private static bool TryGetSharedDimensionEntries(
        Quantity a,
        Quantity b,
        [NotNullWhen(true)] out UnitConversionEntry? left,
        [NotNullWhen(true)] out UnitConversionEntry? right)
    {
        right = null;

        return UnitConversionTable.Instance.TryGet(a.Subtype, out left)
            && UnitConversionTable.Instance.TryGet(b.Subtype, out right)
            && left.SharesDimensionWith(right);
    }

    private static double CanonicalValue(Quantity quantity)
    {
        return UnitConversionTable.Instance.TryGet(quantity.Subtype, out UnitConversionEntry? entry)
            ? entry.CanonicalOf(quantity.BaseValue)
            : quantity.BaseValue;
    }

    /// <summary>
    /// Distance between two quantities on the basis their tier compares on:
    /// typed pairs on the canonical scale (an exactly-equal converted
    /// candidate must not look 1000× farther than a same-subtype one),
    /// wildcard pairs on the as-written values. Every ranker over
    /// <see cref="Equivalent"/> candidates must measure with this — raw
    /// BaseValue distance scores a converted exact match as the farthest.
    /// </summary>
    internal static double RankingDelta(Quantity needle, Quantity candidate)
    {
        return needle.Kind is UnitKind.Plain || candidate.Kind is UnitKind.Plain
            ? Math.Abs(needle.BaseValue - candidate.BaseValue)
            : Math.Abs(CanonicalValue(needle) - CanonicalValue(candidate));
    }

    /// <summary>
    /// Searches the haystack for the best quantity-equivalent match for
    /// <paramref name="needle"/>. Tier 2 (both sides typed) is preferred over
    /// Tier 3 (Plain wildcard). Within a tier, the candidate at the smallest
    /// <see cref="RankingDelta"/> wins; an equal-delta tie prefers the span
    /// written in the needle's own subtype over a converted restatement, and
    /// haystack iteration order breaks what remains. Spans listed in
    /// <paramref name="claimedSpans"/> are skipped — callers use this to
    /// prevent a lower-tier match from displacing a higher-tier one already
    /// wrapped on the same span.
    /// </summary>
    public static QuantityMatch? FindBestMatch(
        Quantity needle,
        IReadOnlyList<Quantity> haystack,
        ISet<(int Start, int End)>? claimedSpans = null)
    {
        QuantityMatch? best = null;
        double bestDelta = double.MaxValue;

        foreach (Quantity candidate in haystack)
        {
            if (claimedSpans is not null && claimedSpans.Contains((candidate.Start, candidate.End)))
            {
                continue;
            }

            if (!Equivalent(needle, candidate))
            {
                continue;
            }

            MatchTier tier = needle.Kind is UnitKind.Plain || candidate.Kind is UnitKind.Plain
                ? MatchTier.PlainWildcard
                : MatchTier.TypedQuantity;

            double delta = RankingDelta(needle, candidate);

            // Lower tier number = higher confidence. Strict tier preference
            // first, distance as tie-break within the tier, subtype identity
            // as the tie-break on equal distance.
            if (best is null
                || tier < best.Tier
                || (tier == best.Tier && delta < bestDelta)
                || (tier == best.Tier && delta == bestDelta
                    && candidate.Subtype == needle.Subtype && best.Quantity.Subtype != needle.Subtype))
            {
                best = new QuantityMatch(tier, candidate);
                bestDelta = delta;
            }
        }

        return best;
    }

    /// <summary>
    /// Convenience for grounding helpers: returns whether <paramref name="text"/>
    /// contains a quantity equivalent to <paramref name="target"/>. Returns false
    /// when <paramref name="target"/> doesn't parse as a single quantity (so
    /// callers don't have to guard themselves). Kind-aware tolerance, Plain
    /// wildcard recovery, Year zero-tolerance, and broad CLDR currency / unit
    /// recognition compared to the prior flat 1% numeric matcher.
    /// </summary>
    public static bool ContainsEquivalent(string text, string target)
    {
        Quantity? targetQuantity = UnitExtractor.TryExtractSingle(target);
        if (targetQuantity is null)
        {
            return false;
        }

        IReadOnlyList<Quantity> textQuantities = UnitExtractor.Extract(text);
        return FindBestMatch(targetQuantity, textQuantities) is not null;
    }

    private static double ToleranceFor(UnitKind kind)
    {
        return ToleranceByKind.TryGetValue(kind, out double t) ? t : DEFAULT_TYPED_TOLERANCE;
    }

    internal static bool WithinTolerance(double a, double b, double tolerance)
    {
        if (tolerance <= 0.0)
        {
            return a == b;
        }

        double max = Math.Max(Math.Abs(a), Math.Abs(b));
        if (max == 0.0)
        {
            return a == b;
        }

        return Math.Abs(a - b) / max < tolerance;
    }
}

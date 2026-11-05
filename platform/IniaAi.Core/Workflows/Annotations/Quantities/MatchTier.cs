namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// Confidence tier of a quantity match. Lower numbers are higher confidence.
/// Higher tiers never displace claims made by lower tiers on the same span.
/// </summary>
public enum MatchTier
{
    /// <summary>
    /// Literal substring of the needle's surface form was found in the
    /// haystack text. Zero ambiguity. Resolved by callers, not by
    /// <see cref="QuantityMatcher"/> itself.
    /// </summary>
    LiteralSubstring = 1,

    /// <summary>
    /// Both sides are typed (non-Plain) with the same Kind, stated in the
    /// same Subtype or in subtypes that convert within one dimension, and
    /// values match within the kind's tolerance.
    /// </summary>
    TypedQuantity = 2,

    /// <summary>
    /// At least one side is Plain (bare number, no unit). Values match within
    /// wildcard tolerance, ignoring kind. Used to recover matches when an AI
    /// rewriter dropped the unit symbol from a numerical claim.
    /// </summary>
    PlainWildcard = 3,
}

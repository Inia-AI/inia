namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// Result of a successful match. <see cref="Tier"/> identifies confidence so
/// callers can apply the "lower tier never displaces higher tier on the same
/// span" rule.
/// </summary>
public sealed record QuantityMatch(MatchTier Tier, Quantity Quantity);

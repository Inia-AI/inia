namespace IniaAi.Core.Workflows.Annotations.Dates;

/// <summary>
/// Whether a value read as a date. <see cref="ShapedButUnreadable"/> is the honest middle: the
/// value is written like a date but no reading of it is valid — a two-digit year, an impossible
/// day, an unrecognised month — so it must not silently degrade to a text comparison.
/// </summary>
public enum DateReadOutcome
{
    Read,
    ShapedButUnreadable,
    NotADate,
}

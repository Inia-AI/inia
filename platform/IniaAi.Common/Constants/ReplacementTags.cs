namespace IniaAi.Common.Constants;

/// <summary>
/// Single source of truth for how replacement tags are compared. Tags resolve case-sensitively
/// (<see cref="StringComparison.Ordinal"/>) so the runtime substitution engine, dependency
/// detection, and the editor/backend validators all agree with the case-sensitive block-output
/// lookup. Change this one value to change the policy everywhere.
/// </summary>
public static class ReplacementTags
{
    public const StringComparison Comparison = StringComparison.Ordinal;
}

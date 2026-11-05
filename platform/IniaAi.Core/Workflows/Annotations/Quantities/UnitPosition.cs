namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// Where the unit form appears relative to the numeric value.
/// Currency symbols/codes are typically prefix; SI/imperial units are suffix.
/// </summary>
internal enum UnitPosition
{
    Either,
    Prefix,
    Suffix,
}

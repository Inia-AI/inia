namespace IniaAi.Core.Workflows.Annotations.Quantities;

/// <summary>
/// Top-level dimension of a quantity. Mirrors the CLDR unit categories
/// plus Currency (which CLDR keeps separate) plus Magnitude (a multiplier
/// like "billion" that combines with another quantity) plus Plain (a bare
/// number with no detectable unit).
/// </summary>
public enum UnitKind
{
    Plain,
    Magnitude,
    Currency,
    Acceleration,
    Angle,
    Area,
    Concentration,
    Consumption,
    Digital,
    Duration,
    Electric,
    Energy,
    Force,
    Frequency,
    Graphics,
    Length,
    Light,
    Mass,
    Power,
    Pressure,
    Speed,
    Temperature,
    Torque,
    Volume,
    Year,
}

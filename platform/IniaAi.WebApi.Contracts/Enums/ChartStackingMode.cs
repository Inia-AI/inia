using System.Runtime.Serialization;

namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Defines the stacking behavior for bar and column charts.
/// </summary>
public enum ChartStackingMode
{
    /// <summary>
    /// Columns/bars are displayed side-by-side (grouped).
    /// </summary>
    [EnumMember(Value = "none")]
    None = 0,

    /// <summary>
    /// Columns/bars are stacked on top of each other.
    /// </summary>
    [EnumMember(Value = "normal")]
    Normal = 1,

    /// <summary>
    /// Columns/bars are stacked with percentage scaling (total always 100%).
    /// </summary>
    [EnumMember(Value = "percent")]
    Percent = 2
}
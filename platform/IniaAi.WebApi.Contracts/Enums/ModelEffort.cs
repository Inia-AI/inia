using System.Runtime.Serialization;

namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// The effort level for adaptive-generation AI models. Controls how eagerly
/// the model spends tokens across thinking, tool calls, and text output.
/// </summary>
public enum ModelEffort
{
    /// <summary>
    /// Most efficient. Significant token savings with some capability reduction.
    /// </summary>
    [EnumMember(Value = "low")]
    Low = 0,

    /// <summary>
    /// Balanced approach with moderate token savings.
    /// </summary>
    [EnumMember(Value = "medium")]
    Medium = 1,

    /// <summary>
    /// High capability; the API default for every model except Opus 5.5, whose default is Medium.
    /// </summary>
    [EnumMember(Value = "high")]
    High = 2,

    /// <summary>
    /// Extended capability for long-horizon work.
    /// </summary>
    [EnumMember(Value = "xhigh")]
    XHigh = 3,

    /// <summary>
    /// Absolute maximum capability with no constraints on token spending.
    /// </summary>
    [EnumMember(Value = "max")]
    Max = 4,
}

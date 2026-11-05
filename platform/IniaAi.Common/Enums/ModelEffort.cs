using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Common.Enums;

/// <summary>
/// The effort level for adaptive-generation Anthropic models
/// (<c>output_config.effort</c> in the Messages API). Controls how eagerly the
/// model spends tokens across thinking, tool calls, and text. Omitting the
/// value leaves the model at its API default (<see cref="High"/> for most models,
/// <see cref="Medium"/> for Opus 5.5).
/// </summary>
public enum ModelEffort
{
    [EnumMember(Value = "low")]
    [Description("Low")]
    Low = 0,

    [EnumMember(Value = "medium")]
    [Description("Medium")]
    Medium = 1,

    [EnumMember(Value = "high")]
    [Description("High")]
    High = 2,

    [EnumMember(Value = "xhigh")]
    [Description("Extra High")]
    XHigh = 3,

    [EnumMember(Value = "max")]
    [Description("Max")]
    Max = 4,
}

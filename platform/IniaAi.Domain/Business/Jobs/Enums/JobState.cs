using System.Runtime.Serialization;

namespace IniaAi.Services.Models.Jobs.Enums;

/// <summary>
/// An enumeration of possible job states.
/// </summary>
public enum JobState
{
    [EnumMember(Value = nameof(None))]
    None,
    [EnumMember(Value = nameof(Queued))]
    Queued,
    [EnumMember(Value = nameof(Preparing))]
    Preparing,
    [EnumMember(Value = nameof(Anonymizing))]
    Anonymizing,
    [EnumMember(Value = nameof(Processing))]
    Processing,
    [EnumMember(Value = nameof(Error))]
    Error,
    [EnumMember(Value = nameof(Cancelled))]
    Cancelled,
    [EnumMember(Value = nameof(Done))]
    Done,
}

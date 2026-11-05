using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentEntryOverrideState
{
    [EnumMember(Value = nameof(None))]
    None = 1,

    [EnumMember(Value = nameof(Hidden))]
    Hidden = 2,

    [EnumMember(Value = nameof(BrokenOut))]
    BrokenOut = 3,
}

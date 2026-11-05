using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentSlotState
{
    [EnumMember(Value = nameof(Hidden))]
    Hidden = 1,

    [EnumMember(Value = nameof(BrokenOut))]
    BrokenOut = 2,
}

using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentState
{
    [EnumMember(Value = nameof(Active))]
    Active = 1,

    [EnumMember(Value = nameof(Retired))]
    Retired = 2,
}

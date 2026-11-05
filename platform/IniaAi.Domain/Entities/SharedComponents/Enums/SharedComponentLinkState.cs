using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentLinkState
{
    [EnumMember(Value = nameof(Linked))]
    Linked = 1,

    [EnumMember(Value = nameof(BrokenOut))]
    BrokenOut = 2,
}

using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentKind
{
    [EnumMember(Value = nameof(Block))]
    Block = 1,

    [EnumMember(Value = nameof(Section))]
    Section = 2,
}

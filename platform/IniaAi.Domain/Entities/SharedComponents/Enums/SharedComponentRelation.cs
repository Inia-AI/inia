using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentRelation
{
    [EnumMember(Value = nameof(InSync))]
    InSync = 1,

    [EnumMember(Value = nameof(LocalAhead))]
    LocalAhead = 2,

    [EnumMember(Value = nameof(HeadAhead))]
    HeadAhead = 3,

    [EnumMember(Value = nameof(Diverged))]
    Diverged = 4,

    [EnumMember(Value = nameof(ComponentMissing))]
    ComponentMissing = 5,

    [EnumMember(Value = nameof(ComponentRetired))]
    ComponentRetired = 6,
}

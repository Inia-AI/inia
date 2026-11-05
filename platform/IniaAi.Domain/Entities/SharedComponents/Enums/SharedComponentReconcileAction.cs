using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentReconcileAction
{
    [EnumMember(Value = nameof(UpdateShared))]
    UpdateShared = 1,

    [EnumMember(Value = nameof(AdoptHead))]
    AdoptHead = 2,

    [EnumMember(Value = nameof(BreakOut))]
    BreakOut = 3,

    [EnumMember(Value = nameof(KeepLocal))]
    KeepLocal = 4,

    [EnumMember(Value = nameof(Relink))]
    Relink = 5,

    [EnumMember(Value = nameof(Hide))]
    Hide = 6,

    [EnumMember(Value = nameof(Unhide))]
    Unhide = 7,
}

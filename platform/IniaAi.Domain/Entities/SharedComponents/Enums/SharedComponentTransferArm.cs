using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentTransferArm
{
    [EnumMember(Value = nameof(TakeTheirs))]
    TakeTheirs = 1,

    [EnumMember(Value = nameof(KeepMine))]
    KeepMine = 2,
}

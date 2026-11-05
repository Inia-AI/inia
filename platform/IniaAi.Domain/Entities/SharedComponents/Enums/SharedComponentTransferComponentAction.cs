using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentTransferComponentAction
{
    [EnumMember(Value = nameof(Created))]
    Created = 1,

    [EnumMember(Value = nameof(Appended))]
    Appended = 2,

    [EnumMember(Value = nameof(Deduped))]
    Deduped = 3,

    [EnumMember(Value = nameof(Kept))]
    Kept = 4,
}

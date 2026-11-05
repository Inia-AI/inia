using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentTransferRelation
{
    [EnumMember(Value = nameof(New))]
    New = 1,

    [EnumMember(Value = nameof(InSync))]
    InSync = 2,

    [EnumMember(Value = nameof(LocalAhead))]
    LocalAhead = 3,

    [EnumMember(Value = nameof(BundleAhead))]
    BundleAhead = 4,

    [EnumMember(Value = nameof(Diverged))]
    Diverged = 5,

    [EnumMember(Value = nameof(LocalRetired))]
    LocalRetired = 6,
}

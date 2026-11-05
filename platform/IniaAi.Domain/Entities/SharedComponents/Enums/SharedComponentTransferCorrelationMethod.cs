using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentTransferCorrelationMethod
{
    [EnumMember(Value = nameof(Anchor))]
    Anchor = 1,

    [EnumMember(Value = nameof(HashTrail))]
    HashTrail = 2,

    [EnumMember(Value = nameof(GuidEquality))]
    GuidEquality = 3,

    [EnumMember(Value = nameof(Create))]
    Create = 4,
}

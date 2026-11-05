using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Documents.Enums;

public enum PageLocation
{
    [EnumMember(Value = nameof(FullPage))]
    FullPage,

    [EnumMember(Value = nameof(LeftSide))]
    LeftSide,

    [EnumMember(Value = nameof(RightSide))]
    RightSide,
}

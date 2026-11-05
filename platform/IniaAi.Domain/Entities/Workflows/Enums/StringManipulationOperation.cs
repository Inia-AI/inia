using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Workflows.Enums;

public enum StringManipulationOperation
{
    [EnumMember(Value = nameof(Replace))]
    Replace = 0,

    [EnumMember(Value = nameof(Substring))]
    Substring = 1,

    [EnumMember(Value = nameof(Trim))]
    Trim = 2,

    [EnumMember(Value = nameof(Split))]
    Split = 3,

    [EnumMember(Value = nameof(Join))]
    Join = 4,

    [EnumMember(Value = nameof(ChangeCase))]
    ChangeCase = 5,

    [EnumMember(Value = nameof(Length))]
    Length = 6,

    [EnumMember(Value = nameof(EscapeSpecialCharacters))]
    EscapeSpecialCharacters = 7,

    [EnumMember(Value = nameof(UnescapeSpecialCharacters))]
    UnescapeSpecialCharacters = 8,

    [EnumMember(Value = nameof(ReplaceUsingPattern))]
    ReplaceUsingPattern = 9,

    [EnumMember(Value = nameof(Distinct))]
    Distinct = 10,
}

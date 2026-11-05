using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.SharedComponents.Enums;

public enum SharedComponentEntryOrigin
{
    [EnumMember(Value = nameof(DefinitionInline))]
    DefinitionInline = 1,

    [EnumMember(Value = nameof(DefinitionRef))]
    DefinitionRef = 2,

    [EnumMember(Value = nameof(LocalInsert))]
    LocalInsert = 3,
}

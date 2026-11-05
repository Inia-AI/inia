using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Workflows.Enums;

public enum SectionEntryKind
{
    [EnumMember(Value = nameof(Inline))]
    Inline = 1,

    [EnumMember(Value = nameof(ComponentRef))]
    ComponentRef = 2,
}

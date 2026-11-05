using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Workflows.Enums;

public enum JsonExtractionMode
{
    [EnumMember(Value = nameof(Single))]
    Single = 0,

    [EnumMember(Value = nameof(XmlTagPattern))]
    XmlTagPattern = 1,

    [EnumMember(Value = nameof(Regex))]
    Regex = 2,
}

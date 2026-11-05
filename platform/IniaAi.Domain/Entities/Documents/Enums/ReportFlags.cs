using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Documents.Enums;

[Flags]
public enum ReportFlags
{
    [EnumMember(Value = nameof(None))]
    None = 0,
    [EnumMember(Value = nameof(Monitoring))]
    Monitoring = 1 << 1,
}

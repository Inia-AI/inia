using System.Runtime.Serialization;

namespace IniaAi.AiConnection.Anthropic.ApiModel.Enums;

internal enum SourceType
{
    [EnumMember(Value = "base64")]
    Base64,

    [EnumMember(Value = "text")]
    Text,
}

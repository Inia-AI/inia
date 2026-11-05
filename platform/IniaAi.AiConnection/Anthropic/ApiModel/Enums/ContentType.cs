using System.Runtime.Serialization;

namespace IniaAi.AiConnection.Anthropic.ApiModel.Enums;

internal enum ContentType
{
    [EnumMember(Value = "image")]
    Image,

    [EnumMember(Value = "text")]
    Text,

    [EnumMember(Value = "text_delta")]
    TextDelta,

    [EnumMember(Value = "thinking")]
    Thinking,

    [EnumMember(Value = "thinking_delta")]
    ThinkingDelta,

    [EnumMember(Value = "document")]
    Document,
}

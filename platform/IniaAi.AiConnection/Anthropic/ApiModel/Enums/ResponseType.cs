using System.Runtime.Serialization;

namespace IniaAi.AiConnection.Anthropic.ApiModel.Enums;

internal enum ResponseType
{
    [EnumMember(Value = "message")]
    Message,
    [EnumMember(Value = "message_stop")]
    MessageStop
}

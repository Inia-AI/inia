using System.Runtime.Serialization;

namespace IniaAi.AiConnection.Enums;

public enum MessageSenderRole
{
    [EnumMember(Value = "user")]
    User,
    [EnumMember(Value = "assistant")]
    Assistant
}

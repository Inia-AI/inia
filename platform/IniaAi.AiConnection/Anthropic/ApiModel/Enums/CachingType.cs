using System.Runtime.Serialization;

namespace IniaAi.AiConnection.Anthropic.ApiModel.Enums;

internal enum CachingType
{
    [EnumMember(Value = "ephemeral")]
    Ephemeral = 0,
}

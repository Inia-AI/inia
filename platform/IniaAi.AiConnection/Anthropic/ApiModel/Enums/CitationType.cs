using System.Runtime.Serialization;

namespace IniaAi.AiConnection.Anthropic.ApiModel.Enums;

internal enum CitationType
{
    [EnumMember(Value = "char_location")]
    CharLocation,

    [EnumMember(Value = "page_location")]
    PageLocation,

    [EnumMember(Value = "content_block_location")]
    ContentBlockLocation,
}

using System.Runtime.Serialization;

namespace IniaAi.AiConnection.Cohere.ApiModel.Embeddings.Enums;

internal enum TruncateType
{
    [EnumMember(Value = "NONE")]
    None,
    [EnumMember(Value = "START")]
    Start,
    [EnumMember(Value = "END")]
    End
}

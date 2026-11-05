using System.Runtime.Serialization;

namespace IniaAi.AiConnection.Anthropic.ApiModel.Enums;

internal enum StopReasonType
{
    [EnumMember(Value = "end_turn")]
    EndTurn,
    [EnumMember(Value = "stop_sequence")]
    StopSequence,
    [EnumMember(Value = "max_tokens")]
    MaxTokens,
    [EnumMember(Value = "refusal")]
    Refusal,
    [EnumMember(Value = "model_context_window_exceeded")]
    ModelContextWindowExceeded
}

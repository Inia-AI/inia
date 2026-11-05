using IniaAi.AiConnection.Anthropic.ApiModel.Enums;
using IniaAi.Common;
using IniaAi.Common.Serialization;
using Newtonsoft.Json;

namespace IniaAi.AiConnection.Anthropic.ApiModel;

internal class ErrorMessage : JsonSerializable<ErrorMessage>
{
    // Kept as text: an error type the enum does not list must not fail the whole parse
    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }

    public ErrorType? ToErrorType()
    {
        return Type is not null && Utils.TryParseEnumFromEnumMemberAttribute(Type, out ErrorType errorType)
            ? errorType
            : null;
    }
}

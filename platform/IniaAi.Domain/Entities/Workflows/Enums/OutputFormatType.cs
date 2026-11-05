using System.ComponentModel;
using System.Runtime.Serialization;

namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// Specifies how an AI Query block's response should be validated and extracted.
/// </summary>
public enum OutputFormatType
{
    [EnumMember(Value = nameof(None))]
    [Description("No validation, raw response only")]
    None = 0,

    [EnumMember(Value = nameof(SingleTag))]
    [Description("Response must contain a single named XML-style tag")]
    SingleTag = 1,

    [EnumMember(Value = nameof(MultiTag))]
    [Description("Response must contain multiple named XML-style tags in any order")]
    MultiTag = 2,

    [EnumMember(Value = nameof(Json))]
    [Description("Response must be valid JSON, optionally matching a JSON Schema")]
    Json = 3,

    [EnumMember(Value = nameof(JsonInTag))]
    [Description("Response must contain a tag whose inner content is valid JSON")]
    JsonInTag = 4,
}

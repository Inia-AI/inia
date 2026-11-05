using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// Per-input extraction config used by <see cref="MergeJsonProperties.InputExtractions"/>.
/// One entry corresponds to a single index in <see cref="MergeJsonProperties.JsonInputs"/>.
/// When <see cref="Mode"/> is <see cref="JsonExtractionMode.Single"/>, the input is parsed as
/// one JSON document (legacy behavior). Otherwise the input is treated as text and sliced
/// into multiple JSON snippets via <see cref="Pattern"/>.
/// </summary>
public class MergeJsonInputExtraction
{
    /// <summary>
    /// How the input value at the corresponding index should be turned into JSON tokens.
    /// </summary>
    [JsonProperty(nameof(Mode))]
    [JsonConverter(typeof(StringEnumConverter))]
    public JsonExtractionMode Mode { get; set; } = JsonExtractionMode.Single;

    /// <summary>
    /// Extraction pattern used when <see cref="Mode"/> is not Single.
    /// For <see cref="JsonExtractionMode.XmlTagPattern"/> this is a tag-name glob (e.g. <c>*</c>, <c>page*</c>).
    /// For <see cref="JsonExtractionMode.Regex"/> this is a regex whose capture group 1 wraps the JSON snippet.
    /// Ignored when <see cref="Mode"/> is Single. Not template-resolved at execution time —
    /// patterns are workflow configuration, not values.
    /// </summary>
    [JsonProperty(nameof(Pattern))]
    public string Pattern { get; set; } = "*";

    public MergeJsonInputExtraction Clone()
    {
        return new MergeJsonInputExtraction
        {
            Mode = Mode,
            Pattern = Pattern,
        };
    }
}

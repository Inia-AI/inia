using IniaAi.Common.Serialization;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;
using IniaAi.Domain.Entities.Workflows.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties;

/// <summary>
/// The settings of a block whose type this build does not register, kept exactly as they were
/// stored. Only the failure setting is read, so a run still stops when the block fails if its author
/// chose that. Nothing rewrites them, and both serializers write them back unchanged, so the block
/// survives a save here and runs again in a build that registers its type.
/// </summary>
[JsonConverter(typeof(UnavailableBlockPropertiesNewtonsoftConverter))]
[System.Text.Json.Serialization.JsonConverter(typeof(UnavailableBlockPropertiesJsonConverter))]
public sealed class UnavailableBlockProperties : BlockProperties
{
    /// <summary>
    /// The <see cref="BlockProperties.Type"/> of settings that name a type no member of
    /// <see cref="BlockTypes"/> has, such as one a later release added. The block keeps its own number.
    /// </summary>
    public const BlockTypes UNNAMED_TYPE = (BlockTypes)(-1);

    private readonly string _storedJson;

    public UnavailableBlockProperties(BlockTypes type, string storedJson)
    {
        Type = type;
        _storedJson = storedJson;
        IsCritical = JsonText.ParseObject(storedJson).TryGetValue(nameof(IsCritical), StringComparison.OrdinalIgnoreCase, out JToken? isCritical)
            && isCritical.Type is JTokenType.Boolean
                ? isCritical.Value<bool>()
                : null;
    }

    /// <summary>A method rather than a property, so the placeholder walk never rewrites the stored text.</summary>
    public string StoredJson()
    {
        return _storedJson;
    }

    /// <summary>
    /// Writes the stored settings as tokens rather than raw text: a JSON tree writer keeps raw text
    /// as an opaque value.
    /// </summary>
    public void WriteTo(JsonWriter writer)
    {
        using JsonTextReader reader = JsonText.CreateReader(_storedJson);
        _ = reader.Read();
        writer.WriteToken(reader);
    }

    public override BlockProperties Clone()
    {
        return new UnavailableBlockProperties(Type, _storedJson);
    }

    public override void SetExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
    }

    public override void UpdateExecutionState(
        bool asPlaceHolder,
        Dictionary<string, string?> inputs,
        StringComparison stringComparison = IniaAi.Common.Constants.ReplacementTags.Comparison)
    {
    }

    public override void ClearExecutionState()
    {
    }
}

public sealed class UnavailableBlockPropertiesNewtonsoftConverter : JsonConverter<UnavailableBlockProperties>
{
    public override bool CanRead => false;

    public override UnavailableBlockProperties ReadJson(
        JsonReader reader,
        Type objectType,
        UnavailableBlockProperties? existingValue,
        bool hasExistingValue,
        JsonSerializer serializer)
    {
        throw new NotSupportedException("Unavailable block settings are read through the block properties converter.");
    }

    public override void WriteJson(JsonWriter writer, UnavailableBlockProperties? value, JsonSerializer serializer)
    {
        if (value is null)
        {
            writer.WriteNull();
            return;
        }

        value.WriteTo(writer);
    }
}

public sealed class UnavailableBlockPropertiesJsonConverter : System.Text.Json.Serialization.JsonConverter<UnavailableBlockProperties>
{
    public override UnavailableBlockProperties Read(
        ref System.Text.Json.Utf8JsonReader reader,
        Type typeToConvert,
        System.Text.Json.JsonSerializerOptions options)
    {
        throw new NotSupportedException("Unavailable block settings are read through the block properties converter.");
    }

    public override void Write(
        System.Text.Json.Utf8JsonWriter writer,
        UnavailableBlockProperties value,
        System.Text.Json.JsonSerializerOptions options)
    {
        writer.WriteRawValue(value.StoredJson());
    }
}

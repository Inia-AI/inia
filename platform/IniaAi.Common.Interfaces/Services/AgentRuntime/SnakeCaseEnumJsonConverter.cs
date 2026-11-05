using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Snake_case enum serialization via the built-in converter, with the one custom behaviour the
/// runtime's enums share: unrecognised or non-string values read as the enum's default member
/// (Unknown) instead of throwing, so a runtime that grows new values keeps working. Apply it at
/// the member level — serializer options that carry their own enum converter (JsonDefaults.Options
/// does) override a type-level attribute, but never a member-level one.
/// </summary>
public sealed class SnakeCaseEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private static readonly JsonConverter<TEnum> _snakeCaseConverter = CreateSnakeCaseConverter();

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            reader.Skip();
            return default;
        }

        try
        {
            return _snakeCaseConverter.Read(ref reader, typeToConvert, options);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        _snakeCaseConverter.Write(writer, value, options);
    }

    private static JsonConverter<TEnum> CreateSnakeCaseConverter()
    {
        var factory = new JsonStringEnumConverter<TEnum>(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false);

        return factory.CreateConverter(typeof(TEnum), new JsonSerializerOptions()) as JsonConverter<TEnum>
            ?? throw new InvalidOperationException(
                $"JsonStringEnumConverter did not produce a converter for {typeof(TEnum).Name}");
    }
}

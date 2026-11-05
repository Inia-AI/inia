using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Services.Converters;

public class DateTimeFormatConverter : JsonConverter<DateTime>
{
    private readonly string _format;

    public DateTimeFormatConverter(string format)
    {
        _format = format;
    }

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        _ = reader.GetString();

        return DateTime.ParseExact(
             reader.GetString()!,
             _format,
             CultureInfo.InvariantCulture);
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer, nameof(writer));

        writer.WriteStringValue(value
            .ToUniversalTime()
            .ToString(_format, CultureInfo.InvariantCulture));
    }
}

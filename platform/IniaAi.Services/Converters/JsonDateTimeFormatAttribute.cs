using System.Text.Json.Serialization;

namespace IniaAi.Services.Converters;

public sealed class JsonDateTimeFormatAttribute : JsonConverterAttribute
{
    public string Format { get; }

    public JsonDateTimeFormatAttribute(string format)
    {
        Format = format;
    }

    public override JsonConverter? CreateConverter(Type typeToConvert)
    {
        return new DateTimeFormatConverter(Format);
    }
}

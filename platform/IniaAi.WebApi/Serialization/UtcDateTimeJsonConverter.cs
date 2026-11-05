using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.WebApi.Serialization;

/// <summary>
/// Writes every <see cref="DateTime"/> with a UTC marker. SQL Server's datetime2 carries no
/// offset, so EF Core reads it back with <see cref="DateTimeKind.Unspecified"/> and the
/// default serializer omits the marker, while Postgres yields <see cref="DateTimeKind.Utc"/>
/// and gets one. A browser reads a zone-less ISO string as local time, so the wire format
/// must not depend on the database provider.
/// </summary>
public sealed class UtcDateTimeJsonConverter : JsonConverter<DateTime>
{
    /// <inheritdoc/>
    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.GetDateTime();
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        DateTime utc = value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        writer.WriteStringValue(utc);
    }
}

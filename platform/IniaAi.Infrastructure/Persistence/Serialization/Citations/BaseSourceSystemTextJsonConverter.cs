using IniaAi.Domain.Entities.Citations.Sources;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Infrastructure.Persistence.Serialization.Citations;

/// <summary>
/// System.Text.Json converter for polymorphic <see cref="BaseSource"/> serialization/deserialization.
/// Used for EF Core database storage.
/// </summary>
public class BaseSourceSystemTextJsonConverter : JsonConverter<BaseSource>
{
    public override BaseSource? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType is JsonTokenType.Null)
        {
            return null;
        }

        using var document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        if (!root.TryGetProperty("type", out JsonElement typeProperty) &&
            !root.TryGetProperty("Type", out typeProperty))
        {
            throw new JsonException("Missing 'type' discriminator property.");
        }

        string? typeStr = typeProperty.GetString();
        if (!Enum.TryParse(typeStr, ignoreCase: true, out CitationSourceType sourceType))
        {
            throw new JsonException($"Unknown source type: {typeStr}");
        }

        string rawText = root.GetRawText();
        return sourceType switch
        {
            CitationSourceType.Document => DeserializeDocumentSource(rawText, options),
            CitationSourceType.Url => DeserializeUrlSource(rawText, options),
            CitationSourceType.Block => DeserializeBlockSource(rawText, options),
            _ => throw new JsonException($"Unhandled source type: {sourceType}")
        };
    }

    private static DocumentSource DeserializeDocumentSource(string json, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        return new DocumentSource
        {
            CitedText = GetStringProperty(root, "citedText") ?? string.Empty,
            DocumentId = GetStringProperty(root, "documentId") ?? string.Empty,
            PageId = GetStringProperty(root, "pageId") ?? string.Empty,
            PageNumber = GetIntProperty(root, "pageNumber"),
            FilePageNumber = GetIntProperty(root, "filePageNumber"),
            DocumentName = GetStringProperty(root, "documentName"),
            Unverified = GetBoolProperty(root, "unverified")
        };
    }

    private static UrlSource DeserializeUrlSource(string json, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;

        return new UrlSource
        {
            CitedText = GetStringProperty(root, "citedText") ?? string.Empty,
            Url = GetStringProperty(root, "url") ?? string.Empty
        };
    }

    private static DocumentSource DeserializeDocumentSourceFromElement(JsonElement root, JsonSerializerOptions options)
    {
        return new DocumentSource
        {
            CitedText = GetStringProperty(root, "citedText") ?? string.Empty,
            DocumentId = GetStringProperty(root, "documentId") ?? string.Empty,
            PageId = GetStringProperty(root, "pageId") ?? string.Empty,
            PageNumber = GetIntProperty(root, "pageNumber"),
            FilePageNumber = GetIntProperty(root, "filePageNumber"),
            DocumentName = GetStringProperty(root, "documentName"),
            Unverified = GetBoolProperty(root, "unverified")
        };
    }

    private static UrlSource DeserializeUrlSourceFromElement(JsonElement root, JsonSerializerOptions options)
    {
        return new UrlSource
        {
            CitedText = GetStringProperty(root, "citedText") ?? string.Empty,
            Url = GetStringProperty(root, "url") ?? string.Empty
        };
    }

    private BlockSource DeserializeBlockSource(string json, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        return DeserializeBlockSourceFromElement(root, options);
    }

    private BlockSource DeserializeBlockSourceFromElement(JsonElement root, JsonSerializerOptions options)
    {
        var blockSource = new BlockSource
        {
            CitedText = GetStringProperty(root, "citedText") ?? string.Empty,
            BlockId = GetStringProperty(root, "blockId") ?? string.Empty,
            BlockTitle = GetStringProperty(root, "blockTitle")
        };

        // Nested source arrays are read by hand: deserializing them through the converter would recurse into it
        ReadSourceArray(root, "upstreamSources", blockSource.UpstreamSources, options);
        ReadSourceArray(root, "matchingSources", blockSource.MatchingSources, options);

        return blockSource;
    }

    private void ReadSourceArray(JsonElement root, string camelCaseName, List<BaseSource> target, JsonSerializerOptions options)
    {
        if ((!root.TryGetProperty(camelCaseName, out JsonElement arrayProp)
                && !root.TryGetProperty(ToPascalCase(camelCaseName), out arrayProp))
            || arrayProp.ValueKind is not JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement item in arrayProp.EnumerateArray())
        {
            BaseSource? source = DeserializeSourceFromElement(item, options);
            if (source is not null)
            {
                target.Add(source);
            }
        }
    }

    private BaseSource? DeserializeSourceFromElement(JsonElement element, JsonSerializerOptions options)
    {
        if (!element.TryGetProperty("type", out JsonElement typeProperty) &&
            !element.TryGetProperty("Type", out typeProperty))
        {
            return null;
        }

        string? typeStr = typeProperty.GetString();
        return !Enum.TryParse(typeStr, ignoreCase: true, out CitationSourceType sourceType)
            ? null
            : sourceType switch
            {
                CitationSourceType.Document => global::IniaAi.Infrastructure.Persistence.Serialization.Citations.BaseSourceSystemTextJsonConverter.DeserializeDocumentSourceFromElement(element, options),
                CitationSourceType.Url => global::IniaAi.Infrastructure.Persistence.Serialization.Citations.BaseSourceSystemTextJsonConverter.DeserializeUrlSourceFromElement(element, options),
                CitationSourceType.Block => DeserializeBlockSourceFromElement(element, options),
                _ => null
            };
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement prop) ||
            element.TryGetProperty(ToPascalCase(propertyName), out prop)
            ? prop.GetString()
            : null;
    }

    private static int GetIntProperty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement prop) ||
            element.TryGetProperty(ToPascalCase(propertyName), out prop)
            ? prop.GetInt32()
            : 0;
    }

    private static bool GetBoolProperty(JsonElement element, string propertyName)
    {
        return (element.TryGetProperty(propertyName, out JsonElement prop)
            || element.TryGetProperty(ToPascalCase(propertyName), out prop))
            && prop.ValueKind == JsonValueKind.True;
    }

    private static string ToPascalCase(string camelCase)
    {
        return string.IsNullOrEmpty(camelCase) ? camelCase : char.ToUpperInvariant(camelCase[0]) + camelCase[1..];
    }

    public override void Write(Utf8JsonWriter writer, BaseSource value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        switch (value)
        {
            case DocumentSource doc:
                WriteProperty(writer, "type", doc.Type.ToString());
                WriteProperty(writer, "citedText", doc.CitedText);
                WriteProperty(writer, "documentId", doc.DocumentId);
                WriteProperty(writer, "pageId", doc.PageId);
                WriteProperty(writer, "pageNumber", doc.PageNumber);
                WriteProperty(writer, "filePageNumber", doc.FilePageNumber);
                if (doc.DocumentName is not null)
                {
                    WriteProperty(writer, "documentName", doc.DocumentName);
                }

                if (doc.Unverified)
                {
                    writer.WriteBoolean("unverified", true);
                }

                break;

            case UrlSource url:
                WriteProperty(writer, "type", url.Type.ToString());
                WriteProperty(writer, "citedText", url.CitedText);
                WriteProperty(writer, "url", url.Url);
                break;

            case BlockSource block:
                WriteProperty(writer, "type", block.Type.ToString());
                WriteProperty(writer, "citedText", block.CitedText);
                WriteProperty(writer, "blockId", block.BlockId);
                if (block.BlockTitle is not null)
                {
                    WriteProperty(writer, "blockTitle", block.BlockTitle);
                }

                WriteSourceArray(writer, "upstreamSources", block.UpstreamSources, options);
                if (block.MatchingSources.Count > 0)
                {
                    WriteSourceArray(writer, "matchingSources", block.MatchingSources, options);
                }

                break;
        }

        writer.WriteEndObject();
    }

    private void WriteSourceArray(Utf8JsonWriter writer, string name, List<BaseSource> sources, JsonSerializerOptions options)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        foreach (BaseSource source in sources)
        {
            Write(writer, source, options);
        }

        writer.WriteEndArray();
    }

    private static void WriteProperty(Utf8JsonWriter writer, string name, string? value)
    {
        writer.WriteString(name, value);
    }

    private static void WriteProperty(Utf8JsonWriter writer, string name, int value)
    {
        writer.WriteNumber(name, value);
    }
}

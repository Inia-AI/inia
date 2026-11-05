using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.OutputFormat;

/// <summary>
/// Pulls named values out of a validated response. Caller must run
/// <see cref="OutputFormatValidator.Validate"/> first; this method does not re-validate.
/// </summary>
public static class OutputFormatExtractor
{
    /// <summary>
    /// Returns a map of {tagOrFieldName -> stringValue}. For Json/JsonInTag, the entry
    /// keyed under <c>"Json"</c> (Json) or under the tag name (JsonInTag) holds the raw
    /// JSON text so callers can also populate <c>BlockResponse.JsonResponse</c>.
    /// </summary>
    public static Dictionary<string, string> Extract(string cleaned, AiQueryProperties props)
    {
        Dictionary<string, string> result = [];

        switch (props.OutputFormatType)
        {
            case OutputFormatType.None:
                return result;

            case OutputFormatType.SingleTag:
                ExtractSingleTag(cleaned, props.OutputFormatTagName, result);
                return result;

            case OutputFormatType.MultiTag:
                ExtractMultiTag(cleaned, props.OutputFormatTagNames, result);
                return result;

            case OutputFormatType.Json:
                result["Json"] = cleaned;
                return result;

            case OutputFormatType.JsonInTag:
                ExtractJsonInTag(cleaned, props.OutputFormatTagName, result);
                return result;

            default:
                return result;
        }
    }

    /// <summary>
    /// For Json/JsonInTag formats, returns the inner JSON string that should be stored
    /// on <c>BlockResponse.JsonResponse</c>. Returns null for tag-only formats.
    /// </summary>
    public static string? GetJsonForBlockResponse(string cleaned, AiQueryProperties props)
    {
        switch (props.OutputFormatType)
        {
            case OutputFormatType.Json:
                return cleaned;

            case OutputFormatType.JsonInTag:
                if (!TagMatcher.IsValidTagName(props.OutputFormatTagName))
                {
                    return null;
                }

                string? inner = TagMatcher.FindFirstTagContent(cleaned, props.OutputFormatTagName!);
                if (inner is null)
                {
                    return null;
                }

                return LenientCleanup.Clean(inner);

            default:
                return null;
        }
    }

    private static void ExtractSingleTag(string cleaned, string? tagName, Dictionary<string, string> result)
    {
        if (!TagMatcher.IsValidTagName(tagName))
        {
            return;
        }

        string? inner = TagMatcher.FindFirstTagContent(cleaned, tagName!);
        if (inner is not null)
        {
            result[tagName!] = inner;
        }
    }

    private static void ExtractMultiTag(string cleaned, string? csvTagNames, Dictionary<string, string> result)
    {
        IReadOnlyList<string> names = TagMatcher.SplitTagNames(csvTagNames);
        foreach (string name in names)
        {
            if (!TagMatcher.IsValidTagName(name))
            {
                continue;
            }

            string? inner = TagMatcher.FindFirstTagContent(cleaned, name);
            if (inner is not null)
            {
                result[name] = inner;
            }
        }
    }

    private static void ExtractJsonInTag(string cleaned, string? tagName, Dictionary<string, string> result)
    {
        if (!TagMatcher.IsValidTagName(tagName))
        {
            return;
        }

        string? inner = TagMatcher.FindFirstTagContent(cleaned, tagName!);
        if (inner is null)
        {
            return;
        }

        result[tagName!] = LenientCleanup.Clean(inner);
    }
}

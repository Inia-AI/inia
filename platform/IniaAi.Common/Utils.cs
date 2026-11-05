using IniaAi.Common.Enums;
using IniaAi.Common.Numerics;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.Common;

public static partial class Utils
{
    public static readonly string[] NaValues =
    [
        "N/A",
        "NA",
        "N.A.",
        "N.A",
        "NaN",
        "null"
    ];

    public static string DictionaryToString(Dictionary<string, string> dictionary)
    {
        var sb = new StringBuilder();

        foreach ((string key, string value) in dictionary)
        {
            _ = sb.Append($"{key}\n\n{value}\n\n");
        }

        return sb.ToString().Trim('\n');
    }

    public static string? GetStringFromTag(
        string? text,
        string tag,
        bool processMultipleTags = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        tag = tag.ToLowerInvariant();
        string loweredText = text.ToLowerInvariant();
        string openTag = $"<{tag}>";
        string closeTag = $"</{tag}>";

        if (!processMultipleTags)
        {
            return ExtractContentBetweenSingleTags(
                text,
                loweredText,
                openTag,
                closeTag);
        }

        List<string> results = ExtractContentBetweenMultipleTags(
            text,
            loweredText,
            openTag,
            closeTag);

        return results.Count > 0 ? string.Join("\n", results) : null;
    }

    /// <summary>
    /// Reads the LAST tag pair rather than the first: a model response that inventories or
    /// quotes the judged content can echo the output tags early, so the genuine pair is the
    /// trailing one. A missing or unclosed tag returns null.
    /// </summary>
    public static string? GetStringFromLastTag(string? text, string tag)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        int lastOpenIndex = text.LastIndexOf($"<{tag}>", StringComparison.OrdinalIgnoreCase);

        return lastOpenIndex < 0
            ? null
            : GetStringFromTag(text[lastOpenIndex..], tag);
    }

    public static bool IsNaValue(string? value, string[]? additionalNaValues = null)
    {
        return string.IsNullOrWhiteSpace(value)
            || NaValues.Any(naValue => string.Equals(
                value,
                naValue,
                StringComparison.OrdinalIgnoreCase))
            || (additionalNaValues is not null
                && additionalNaValues.Any(additionalNaValue =>
                    string.Equals(
                        value,
                        additionalNaValue,
                        StringComparison.OrdinalIgnoreCase)));
    }

    private static List<string> ExtractContentBetweenMultipleTags(
        string text,
        string loweredText,
        string openTag,
        string closeTag)
    {
        List<string> results = [];
        int startPos = 0;

        while (true)
        {
            int start = loweredText.IndexOf(openTag, startPos);

            if (start == -1)
            {
                break;
            }

            int contentStart = start + openTag.Length;
            int end = loweredText.IndexOf(closeTag, contentStart);

            if (end == -1)
            {
                break;
            }

            int nextOpenTag = loweredText.IndexOf(openTag, contentStart);
            if (nextOpenTag == -1 || nextOpenTag >= end)
            {
                results.Add(text[contentStart..end].Trim());
                startPos = end + closeTag.Length;
            }
            else
            {
                startPos = start + openTag.Length;
            }
        }

        return results;
    }

    private static string? ExtractContentBetweenSingleTags(
        string text,
        string loweredText,
        string openTag,
        string closeTag)
    {
        int searchPos = 0;

        while (true)
        {
            int start = loweredText.IndexOf(openTag, searchPos);

            if (start == -1)
            {
                return null;
            }

            int contentStart = start + openTag.Length;
            int end = loweredText.IndexOf(closeTag, contentStart);

            if (end != -1)
            {
                int nextOpenTag = loweredText.IndexOf(openTag, contentStart);
                if (nextOpenTag == -1 || nextOpenTag >= end)
                {
                    return text[contentStart..end].Trim();
                }
            }

            searchPos = start + openTag.Length;
        }
    }

    public static string? SetStringToTag(
        string? rawResponse,
        string propertyTag,
        string value,
        bool shouldAppend = false)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return shouldAppend
                ? $"<{propertyTag}>\n{value}\n</{propertyTag}>"
                : rawResponse;
        }

        string tag = $"<{propertyTag}>";
        string endTag = $"</{propertyTag}>";
        string loweredText = rawResponse.ToLowerInvariant();

        int start = loweredText.IndexOf(tag);

        if (start == -1)
        {
            return shouldAppend
                ? rawResponse + $"\n\n{tag}\n{value}\n{endTag}"
                : rawResponse;
        }

        int end = loweredText.IndexOf(endTag);

        if (end == -1)
        {
            return rawResponse;
        }

        start += tag.Length;

        string newResponse = $"{rawResponse[..start]}\n{value}\n{rawResponse[end..]}";

        return newResponse;
    }

    public static int ConvertToInt(string? value)
    {
        if (value is not null
            && value.StartsWith('(')
            && value.EndsWith(')'))
        {
            value = "-" + value[1..^1];
        }

        return int.TryParse(value, out int result)
            ? result
            : -1;
    }

    public static double ConvertToDouble(string? value)
    {
        if (value is not null
            && value.StartsWith('(')
            && value.EndsWith(')'))
        {
            value = "-" + value[1..^1];
        }

        return double.TryParse(value, out double result)
            ? result
            : double.NaN;
    }

    /// <summary>
    /// Parses a string of numbers in the format "1, 2, 3, 4, 5, 6-10, 11,
    /// 12, 13-15" into a list of integers.
    /// </summary>
    /// <param name="numbers">The string of numbers to parse.</param>
    /// <param name="skipInvalidSegments">When true, comma-split segments that don't match
    /// the strict number/range shape are silently skipped instead of failing the entire parse.
    /// Useful when the input is composed from multiple variables and a single bad variable
    /// shouldn't drop the valid ones.</param>
    /// <returns>A list of integers.</returns>
    public static List<int> ParseStringForNumbersList(string numbers, bool skipInvalidSegments = false)
    {
        if (string.IsNullOrWhiteSpace(numbers))
        {
            return [];
        }

        numbers = NumericSanitizer.NormaliseDashes(numbers.Replace(" ", ""));

        if (!skipInvalidSegments && !NumbersPatternRegex().IsMatch(numbers))
        {
            return [];
        }

        List<int> pageList = [];

        foreach (string part in numbers.Split(','))
        {
            Match match = NumbersPartRegex().Match(part);
            bool hasEnd = match.Success && match.Groups[2].Success;
            int end = 0;

            if (!match.Success
                || !int.TryParse(match.Groups[1].Value, out int start)
                || (hasEnd && !int.TryParse(match.Groups[2].Value, out end))
                || (hasEnd && start > end))
            {
                if (skipInvalidSegments)
                {
                    continue;
                }

                return [];
            }

            if (hasEnd)
            {
                for (int i = start; i <= end; i++)
                {
                    pageList.Add(i);
                }
            }
            else
            {
                pageList.Add(start);
            }
        }

        return pageList;
    }

    /// <summary>
    /// Removes duplicate pages from a string of pages.
    /// </summary>
    /// <param name="input"></param>
    /// <returns></returns>
    public static string? RemoveDuplicatePages(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return input;
        }

        Dictionary<string, HashSet<string>> seenPages = [];

        string result = DocumentPageTagPattern().Replace(input, match => EvaluatePageMatch(match, seenPages));

        result = NewLinesRegex().Replace(result, "\r\n\r\n");

        return result.Trim();
    }

    /// <summary>
    /// Tries to parse an enum from an EnumMember attribute value.
    /// </summary>
    /// <typeparam name="T">An enum type.</typeparam>
    /// <param name="enumMemberValue">The value of the EnumMember attribute.</param>
    /// <param name="resultingEnum">The resulting enum.</param>
    /// <returns><see langword="true"/> if the enum was successfully parsed;
    /// otherwise, <see langword="false"/>.</returns>
    public static bool TryParseEnumFromEnumMemberAttribute<T>(
        string enumMemberValue,
        out T resultingEnum)
        where T : Enum
    {
        foreach (MemberInfo member in typeof(T).GetMembers())
        {
            EnumMemberAttribute? attribute = member
                .GetCustomAttribute<EnumMemberAttribute>();

            if (attribute is not null
                && attribute.Value is not null
                && attribute.Value.Equals(enumMemberValue, StringComparison.OrdinalIgnoreCase))
            {
                resultingEnum = (T)Enum.Parse(typeof(T), member.Name);
                return true;
            }
        }

        resultingEnum = default!;
        return false;
    }

    public static MediaType DetermineMediaType(string fileName)
    {
        string extension = Path.GetExtension(fileName).ToLowerInvariant();

        return DetermineMediaTypeFromExtension(extension);
    }

    public static MediaType DetermineMediaTypeFromExtension(string extension)
    {
        return extension.ToLower() switch
        {
            // Documents
            ".pdf" => MediaType.ApplicationPdf,
            ".doc" => MediaType.ApplicationMsWord,
            ".docx" => MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument,
            ".xls" => MediaType.ApplicationVndMsExcel,
            ".xlsx" => MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet,
            ".xlsm" => MediaType.ApplicationVndMsExcelSheetMacroEnabled12,
            ".ppt" => MediaType.ApplicationVndMsPowerpoint,
            ".pptx" => MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentPresentationMlPresentation,
            ".odt" => MediaType.ApplicationVndOasisOpendocumentText,
            ".ods" => MediaType.ApplicationVndOasisOpendocumentSpreadsheet,
            ".odp" => MediaType.ApplicationVndOasisOpendocumentPresentation,

            // Text files
            ".txt" => MediaType.TextPlain,
            ".md" or ".markdown" => MediaType.TextMarkdown,
            ".csv" => MediaType.TextCsv,
            ".rtf" => MediaType.ApplicationRtf,
            ".html" or ".htm" => MediaType.TextHtml,
            ".css" => MediaType.TextCss,
            ".js" => MediaType.ApplicationJavascript,
            ".ts" => MediaType.TextTypescript,

            // Email files
            ".eml" => MediaType.MessageRfc822,
            ".msg" => MediaType.ApplicationVndMsOutlook,

            // Data formats
            ".json" => MediaType.ApplicationJson,
            ".xml" => MediaType.ApplicationXml,
            ".yaml" or ".yml" => MediaType.ApplicationXYaml,

            // Images
            ".jpg" or ".jpeg" => MediaType.ImageJpeg,
            ".png" => MediaType.ImagePng,
            ".gif" => MediaType.ImageGif,
            ".bmp" => MediaType.ImageBmp,
            ".svg" => MediaType.ImageSvg,
            ".webp" => MediaType.ImageWebp,
            ".ico" => MediaType.ImageIcon,
            ".tiff" or ".tif" => MediaType.ImageTiff,

            // Audio
            ".mp3" => MediaType.AudioMp3,
            ".wav" => MediaType.AudioWav,
            ".ogg" => MediaType.AudioOgg,
            ".aac" => MediaType.AudioAac,
            ".m4a" => MediaType.AudioMp4,

            // Video
            ".mp4" => MediaType.VideoMp4,
            ".avi" => MediaType.VideoAvi,
            ".mkv" => MediaType.VideoMkv,
            ".mov" => MediaType.VideoQuicktime,
            ".webm" => MediaType.VideoWebm,

            // Archives
            ".zip" => MediaType.ApplicationZip,
            ".rar" => MediaType.ApplicationRar,
            ".7z" => MediaType.Application7z,
            ".tar" => MediaType.ApplicationTar,
            ".gz" => MediaType.ApplicationGzip,

            // Programming files
            ".cs" => MediaType.TextCSharp,
            ".py" => MediaType.TextPython,
            ".java" => MediaType.TextJava,
            ".cpp" or ".cc" => MediaType.TextCpp,
            ".c" => MediaType.TextC,
            ".h" => MediaType.TextCHeader,

            _ => MediaType.Unknown,
        };
    }

    public static string ConvertIntToExcelColumnLetters(int column)
    {
        string columnLetter = string.Empty;

        while (column > 0)
        {
            int remainder = (column - 1) % 26;
            columnLetter = (char)(65 + remainder) + columnLetter;
            column = (column - remainder) / 26;
        }

        return columnLetter;
    }

    public static int ConvertExcelColumnLettersToInt(string column)
    {
        int columnInt = 0;

        foreach (char c in column)
        {
            columnInt = (columnInt * 26) + c - 'A' + 1;
        }

        return columnInt < 1
            ? throw new ArgumentException("Invalid column name")
            : columnInt > 16384
                ? throw new ArgumentException("Column name is too large")
                : columnInt;
    }

    public static string? CleanWhitespace(string? text)
    {
        if (text is null)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        text = RemoveExtraSpacesRegex().Replace(text, " ");
        text = RemoveExtraLineBreaksRegex().Replace(text, "\n\n");

        return text;
    }

    /// <summary>
    /// Gets the first N words from a string while preserving the original whitespace.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <param name="wordCount">The number of words to extract.</param>
    /// <returns>The first N words with original whitespace preserved.</returns>
    public static string GetFirstNWords(string? input, int wordCount)
    {
        if (string.IsNullOrEmpty(input) || wordCount <= 0)
        {
            return string.Empty;
        }

        int count = 0;
        int i = 0;
        bool inWord = false;

        while (i < input.Length)
        {
            if (char.IsWhiteSpace(input[i]))
            {
                if (inWord && count >= wordCount)
                {
                    break;
                }

                inWord = false;
            }
            else
            {
                if (!inWord)
                {
                    count++;
                }

                inWord = true;
            }

            i++;
        }

        return input[..i];
    }

    /// <summary>
    /// Gets the last N words from a string while preserving the original whitespace.
    /// </summary>
    /// <param name="input">The input string.</param>
    /// <param name="wordCount">The number of words to extract.</param>
    /// <returns>The last N words with original whitespace preserved.</returns>
    public static string GetLastNWords(string? input, int wordCount)
    {
        if (string.IsNullOrEmpty(input) || wordCount <= 0)
        {
            return string.Empty;
        }

        int count = 0;
        int i = input.Length - 1;
        bool inWord = false;

        while (i >= 0)
        {
            if (char.IsWhiteSpace(input[i]))
            {
                if (inWord && count >= wordCount)
                {
                    break;
                }

                inWord = false;
            }
            else
            {
                if (!inWord)
                {
                    count++;
                }

                inWord = true;
            }

            i--;
        }

        // Move forward past any leading whitespace we captured
        i++;
        while (i < input.Length && char.IsWhiteSpace(input[i]))
        {
            i++;
        }

        return input[i..];
    }

    private static string EvaluatePageMatch(Match match, Dictionary<string, HashSet<string>> seenPages)
    {
        string fileName = match.Groups[1].Value;
        string pageNumber = match.Groups[2].Value;

        if (!seenPages.TryGetValue(fileName, out HashSet<string>? value))
        {
            value = [];
            seenPages[fileName] = value;
        }

        return value.Add(pageNumber) ? match.Value : "";
    }

    [GeneratedRegex(@"^(-?\d+(-(-?\d+))?,?)+$")]
    private static partial Regex NumbersPatternRegex();

    [GeneratedRegex(@"^(-?\d+)(?:-(-?\d+))?$")]
    private static partial Regex NumbersPartRegex();

    [GeneratedRegex(@"(\r?\n){3,}")]
    private static partial Regex NewLinesRegex();

    [GeneratedRegex(@"<([^>]+)-page(\d+)>([\s\S]*?)</\1-page\2>")]
    private static partial Regex DocumentPageTagPattern();

    [GeneratedRegex(@" {2,}")]
    private static partial Regex RemoveExtraSpacesRegex();

    [GeneratedRegex(@"(\r\n|\r|\n){2,}")]
    private static partial Regex RemoveExtraLineBreaksRegex();
}

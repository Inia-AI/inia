using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.Core.Common;

public static partial class TextSanitizer
{
    /// <summary>
    /// Removes control characters (ASCII 0–31 except for line breaks) and trims excess whitespace.
    /// </summary>
    public static string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        // Replace all control characters except \r (CR) and \n (LF)
        string cleaned = GenerateNonControlCharRegex().Replace(input, "");

        // Optionally normalize line endings to \n
        cleaned = cleaned.Replace("\r\n", "\n").Replace("\r", "\n");

        // Trim leading/trailing whitespace on each line
        var sb = new StringBuilder();
        using var reader = new StringReader(cleaned);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            _ = sb.AppendLine(line.Trim());
        }

        return sb.ToString().Trim();
    }

    [GeneratedRegex(@"[^\P{C}\r\n]+")]
    private static partial Regex GenerateNonControlCharRegex();
}

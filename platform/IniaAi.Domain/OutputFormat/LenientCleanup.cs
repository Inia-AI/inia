using System.Text.RegularExpressions;

namespace IniaAi.Domain.OutputFormat;

/// <summary>
/// Strips a single wrapping markdown code fence (```json ... ``` or ``` ... ```) and
/// trims leading/trailing whitespace. Idempotent.
/// </summary>
public static partial class LenientCleanup
{
    [GeneratedRegex(@"^\s*```(?:[A-Za-z0-9_-]+)?\s*\r?\n([\s\S]*?)\r?\n```\s*$", RegexOptions.Compiled)]
    private static partial Regex CodeFencePattern();

    public static string Clean(string raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        string trimmed = raw.Trim();
        Match match = CodeFencePattern().Match(trimmed);
        if (match.Success)
        {
            return match.Groups[1].Value.Trim();
        }

        return trimmed;
    }
}

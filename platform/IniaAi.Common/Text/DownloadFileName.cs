namespace IniaAi.Common.Text;

/// <summary>
/// The download name an export ships under: the name made filesystem-safe, so a folder of
/// exports stays tellable-apart. Falls back to the id-prefixed pattern only when nothing of the
/// name survives sanitization.
/// </summary>
public static class DownloadFileName
{
    // The characters Windows refuses in a filename — the strictest consumer of the download,
    // whatever OS serves it. The server-side Path.GetInvalidFileNameChars() is useless here:
    // on Linux it only lists '/' and NUL.
    private const string WINDOWS_INVALID_FILENAME_CHARS = "\\/:*?\"<>|";
    private const int MAX_NAME_LENGTH = 120;
    private const int FALLBACK_ID_LENGTH = 8;

    /// <summary>The sanitized name with <paramref name="extension"/>, or the fallback prefix and the id's head when nothing of the name survives.</summary>
    public static string Build(string? name, string id, string fallbackPrefix, string extension)
    {
        char[] cleaned = (name ?? string.Empty)
            .Select(c => char.IsControl(c) || WINDOWS_INVALID_FILENAME_CHARS.Contains(c) ? ' ' : c)
            .ToArray();
        string sanitized = string.Join(' ', new string(cleaned).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        sanitized = sanitized.Truncate(MAX_NAME_LENGTH).TrimEnd('.', ' ');
        if (sanitized.Length > 0)
        {
            return $"{sanitized}{extension}";
        }

        string idHead = id[..Math.Min(FALLBACK_ID_LENGTH, id.Length)];
        return idHead.Length == 0 ? $"{fallbackPrefix}{extension}" : $"{fallbackPrefix}-{idHead}{extension}";
    }
}

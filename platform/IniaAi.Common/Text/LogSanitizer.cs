namespace IniaAi.Common.Text;

/// <summary>
/// Produces log-safe representations of user-supplied strings. Use this before
/// passing any value that originated from an HTTP request (query string, route,
/// body) into an <c>ILogger</c> template parameter to prevent log-forging
/// attacks, where a caller injects newline characters to fabricate additional
/// log lines.
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Returns <paramref name="value"/> with CR, LF, and other ASCII control
    /// characters stripped so it is safe to emit as a log-template parameter.
    /// Returns an empty string when <paramref name="value"/> is null so the log
    /// template still produces a readable output.
    /// </summary>
    /// <param name="value">The raw user-supplied value.</param>
    /// <returns>A log-safe copy of <paramref name="value"/>.</returns>
    public static string ForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char ch in value)
        {
            if (!char.IsControl(ch))
            {
                _ = builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}

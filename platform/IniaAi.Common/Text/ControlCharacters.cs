namespace IniaAi.Common.Text;

/// <summary>
/// Keeps a stored name on one line. A name that a user supplies, such as an uploaded file name,
/// can carry a line break or a tab into every log line, report and listing that shows it.
/// </summary>
public static class ControlCharacters
{
    /// <summary>
    /// Returns <paramref name="value"/> with every control character replaced by a space, so the
    /// words on either side of a line break stay apart.
    /// </summary>
    public static string ToSpaces(string value)
    {
        return string.Create(value.Length, value, static (characters, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                characters[i] = char.IsControl(source[i]) ? ' ' : source[i];
            }
        });
    }
}

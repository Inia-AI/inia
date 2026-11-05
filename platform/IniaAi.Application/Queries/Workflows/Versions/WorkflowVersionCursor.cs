using System.Globalization;

namespace IniaAi.Application.Queries.Workflows.Versions;

/// <summary>
/// The history keyset position <c>(CreatedAt, Id)</c> as one opaque token. Ticks keep the
/// timestamp exact, which a formatted date in a query string does not guarantee.
/// </summary>
public readonly record struct WorkflowVersionCursor(DateTime CreatedAtUtc, string VersionId)
{
    private const char SEPARATOR = '_';

    public string Encode()
    {
        return string.Create(CultureInfo.InvariantCulture, $"{CreatedAtUtc.Ticks}{SEPARATOR}{VersionId}");
    }

    public static bool TryParse(string? token, out WorkflowVersionCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrEmpty(token))
        {
            return false;
        }

        int separatorIndex = token.IndexOf(SEPARATOR, StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex == token.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(token.AsSpan(0, separatorIndex), NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
            || ticks < DateTime.MinValue.Ticks
            || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        cursor = new WorkflowVersionCursor(new DateTime(ticks, DateTimeKind.Utc), token[(separatorIndex + 1)..]);

        return true;
    }
}

using System.Globalization;
using System.Text;

namespace IniaAi.Application.Queries.ReportFeedback;

/// <summary>
/// The opaque page cursor of the cross-organization feedback read: the <c>(CreatedAt, Id)</c>
/// keyset position of the last item on a page, encoded so a client passes it back untouched.
/// </summary>
public static class ReportFeedbackCursor
{
    private const char SEPARATOR = '|';

    public static string Encode(DateTime createdAtUtc, string id)
    {
        string plain = $"{createdAtUtc.Ticks.ToString(CultureInfo.InvariantCulture)}{SEPARATOR}{id}";

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(plain));
    }

    public static bool TryDecode(string? cursor, out DateTime createdAtUtc, out string id)
    {
        createdAtUtc = default;
        id = string.Empty;

        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        string plain;
        try
        {
            plain = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        }
        catch (FormatException)
        {
            return false;
        }

        int separatorIndex = plain.IndexOf(SEPARATOR);
        if (separatorIndex <= 0 || separatorIndex == plain.Length - 1)
        {
            return false;
        }

        if (!long.TryParse(plain[..separatorIndex], NumberStyles.None, CultureInfo.InvariantCulture, out long ticks)
            || ticks > DateTime.MaxValue.Ticks)
        {
            return false;
        }

        createdAtUtc = new DateTime(ticks, DateTimeKind.Utc);
        id = plain[(separatorIndex + 1)..];

        return true;
    }
}

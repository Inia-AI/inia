namespace IniaAi.Common.Text;

/// <summary>
/// Masks credential-shaped values for safe display in logs and clipboard exports.
/// Returns "****" for null/whitespace/very-short inputs; otherwise reveals at most
/// the first three characters (capped regardless of input length) and replaces the
/// rest with asterisks. The fixed-prefix cap keeps disclosure bounded for long
/// secrets while still leaving enough characters to recognise the value at a
/// glance (e.g. "ant***...", "sk-***...").
/// </summary>
public static class SecretMasker
{
    private const int VISIBLE_PREFIX_LENGTH = 3;

    public static string Mask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length <= VISIBLE_PREFIX_LENGTH + 1)
        {
            return "****";
        }

        return value[..VISIBLE_PREFIX_LENGTH] + new string('*', value.Length - VISIBLE_PREFIX_LENGTH);
    }
}

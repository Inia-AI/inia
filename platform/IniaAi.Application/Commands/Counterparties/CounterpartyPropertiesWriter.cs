using IniaAi.Domain.Entities.Counterparties;

namespace IniaAi.Application.Commands.Counterparties;

/// <summary>
/// Applies a request's freeform property bag onto a counterparty. A <c>null</c> request leaves the
/// stored bag untouched (partial update); a supplied bag replaces it wholesale, dropping blank keys and
/// trimming key whitespace. An empty result clears the bag.
/// </summary>
public static class CounterpartyPropertiesWriter
{
    public static void Apply(Counterparty counterparty, IReadOnlyDictionary<string, string>? requested)
    {
        if (requested is null)
        {
            return;
        }

        counterparty.Properties = Clean(requested);
    }

    /// <summary>
    /// The bag as it is stored: blank keys dropped, keys trimmed, a null value made blank, keyed
    /// case-insensitively. Null when nothing remains, or when nothing was supplied.
    /// </summary>
    public static Dictionary<string, string>? Clean(IReadOnlyDictionary<string, string>? requested)
    {
        if (requested is null)
        {
            return null;
        }

        var cleaned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach ((string key, string? value) in requested)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            cleaned[key.Trim()] = value ?? string.Empty;
        }

        return cleaned.Count == 0 ? null : cleaned;
    }
}

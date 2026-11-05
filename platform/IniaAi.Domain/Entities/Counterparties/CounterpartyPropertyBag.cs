namespace IniaAi.Domain.Entities.Counterparties;

/// <summary>
/// The counterparty freeform Properties bag is a case-insensitive map. Author-coined keys are read by
/// <c>{Counterparty.key}</c> placeholders and written by blocks in whatever casing each happens to use,
/// and the API camelCases keys outbound, so key case is never stable. Normalizing to OrdinalIgnoreCase at
/// every assignment makes case-insensitivity the single enforced invariant — so reads are plain lookups
/// and writes upsert in place, with no per-site case-folding.
/// </summary>
public static class CounterpartyPropertyBag
{
    public static Dictionary<string, string>? Normalize(Dictionary<string, string>? source)
    {
        if (source is null)
        {
            return null;
        }

        if (ReferenceEquals(source.Comparer, StringComparer.OrdinalIgnoreCase))
        {
            return source;
        }

        Dictionary<string, string> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> entry in source)
        {
            normalized[entry.Key] = entry.Value;
        }

        return normalized;
    }
}

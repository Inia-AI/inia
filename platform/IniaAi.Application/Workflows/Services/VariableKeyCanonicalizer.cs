using System.Text.Json;
using IniaAi.Domain.Entities.Workflows;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Workflows.Services;

/// <summary>
/// Normalizes submitted workflow-variable dictionary keys to their canonical PascalCase
/// <see cref="WorkflowVariable.ReplacementTag"/>.
/// <para>
/// Variables persisted on a run are camelCased on the way out of the API (the MVC response
/// serializer applies <c>DictionaryKeyPolicy = CamelCase</c>). On an edit-and-re-run those
/// camelCased keys are resubmitted, so a supplied <c>interimPeriod</c> no longer matches the
/// template's PascalCase <c>InterimPeriod</c>. Case-sensitive downstream matching then treats
/// the variable as missing (injecting the default and orphaning the submitted value) or, when a
/// case-insensitive step runs next, collides on the twin keys. Canonicalizing on ingest makes the
/// merge, the validator, and downstream resolution agree on ONE key per variable.
/// </para>
/// </summary>
public static class VariableKeyCanonicalizer
{
    /// <summary>
    /// Returns a dictionary whose keys are the canonical <see cref="WorkflowVariable.ReplacementTag"/>
    /// for every supplied key that matches a template variable case-insensitively. Keys that match no
    /// template variable are carried through verbatim. When the same variable is supplied under more
    /// than one casing, an exact-case match wins; otherwise the first key in ordinal order wins and the
    /// rest are discarded (logged as a warning). Default injection is intentionally NOT performed here.
    /// </summary>
    /// <param name="supplied">The submitted variable values, keyed by however the client cased them.</param>
    /// <param name="variables">The template's variable definitions supplying the canonical tags.</param>
    /// <param name="logger">Optional logger used to warn when a duplicate-casing value is discarded.</param>
    /// <returns>A new dictionary keyed by canonical tags (plus verbatim passthrough keys).</returns>
    public static Dictionary<string, JsonElement> CanonicalizeVariableKeys(
        IReadOnlyDictionary<string, JsonElement>? supplied,
        IEnumerable<WorkflowVariable> variables,
        ILogger? logger = null)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        if (supplied is null || supplied.Count == 0)
        {
            return result;
        }

        // Case-insensitive tag -> canonical-casing lookup (no per-key lowercased-string allocations).
        // Assume tags are unique case-insensitively within a workflow; if ever violated, the first
        // tag in enumeration order wins (no throw).
        var canonicalByTag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (WorkflowVariable variable in variables)
        {
            if (string.IsNullOrEmpty(variable.ReplacementTag))
            {
                continue;
            }

            _ = canonicalByTag.TryAdd(variable.ReplacementTag, variable.ReplacementTag);
        }

        // Group every supplied key by the canonical key it resolves to (its matching tag, else itself).
        var groupsByCanonical = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (string suppliedKey in supplied.Keys)
        {
            string canonicalKey = canonicalByTag.TryGetValue(suppliedKey, out string? tag)
                ? tag
                : suppliedKey;

            if (!groupsByCanonical.TryGetValue(canonicalKey, out List<string>? keys))
            {
                keys = [];
                groupsByCanonical[canonicalKey] = keys;
            }

            keys.Add(suppliedKey);
        }

        foreach ((string canonicalKey, List<string> keys) in groupsByCanonical)
        {
            string chosenKey = keys.Count == 1
                ? keys[0]
                : keys.FirstOrDefault(k => string.Equals(k, canonicalKey, StringComparison.Ordinal))
                    ?? keys.OrderBy(k => k, StringComparer.Ordinal).First();

            result[canonicalKey] = supplied[chosenKey];

            if (keys.Count <= 1)
            {
                continue;
            }

            foreach (string discardedKey in keys.Where(k => !string.Equals(k, chosenKey, StringComparison.Ordinal)))
            {
                logger?.LogWarning(
                    "Duplicate workflow variable key casing for '{CanonicalKey}': discarded submitted key '{DiscardedKey}' in favour of '{ChosenKey}'.",
                    canonicalKey,
                    discardedKey,
                    chosenKey);
            }
        }

        return result;
    }
}

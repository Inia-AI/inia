using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Pure list mutations for the parameterized-section variant document-class picker.
/// "Any" and "None" are mutually exclusive sentinels; an empty selection means "Any"
/// (all documents), matching how the workflow engine interprets an empty filter.
/// </summary>
public static class VariantDocumentClassEditor
{
    public const string ANY = DocumentClass.KEY_ANY;
    public const string NONE = DocumentClass.KEY_NONE;

    /// <summary>True for the "Any"/"None" sentinel values, false for real document classes.</summary>
    public static bool IsSentinel(string documentClass)
    {
        return documentClass is ANY or NONE;
    }

    /// <summary>
    /// Adds a class, enforcing Any/None mutual exclusion. Selecting "Any" or "None"
    /// replaces the whole selection with just that sentinel; selecting a real class
    /// strips both sentinels first. No-op on blank input or an already-present value.
    /// </summary>
    public static void Add(List<string> documentClasses, string value)
    {
        if (string.IsNullOrEmpty(value) || documentClasses.Contains(value))
        {
            return;
        }

        if (value is ANY or NONE)
        {
            documentClasses.Clear();
            documentClasses.Add(value);
        }
        else
        {
            _ = documentClasses.Remove(ANY);
            _ = documentClasses.Remove(NONE);
            documentClasses.Add(value);
        }
    }

    /// <summary>
    /// Removes a class by value. If the selection ends up empty, falls back to "Any"
    /// (all documents) so the picker never lands on an unintended "no documents" state.
    /// </summary>
    public static void Remove(List<string> documentClasses, string value)
    {
        _ = documentClasses.Remove(value);

        if (documentClasses.Count == 0)
        {
            documentClasses.Add(ANY);
        }
    }
}

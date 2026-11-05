namespace IniaAi.Domain.Entities.Workflows;

internal static class ReplacementTagRenames
{
    internal static (Dictionary<string, string?> ToTemp, Dictionary<string, string?> ToFinal) BuildSimultaneousMaps(
        IReadOnlyDictionary<string, string> renames)
    {
        Dictionary<string, string?> toTemp = new();
        Dictionary<string, string?> toFinal = new();
        foreach ((string oldTag, string newTag) in renames)
        {
            string temp = $"tag_rename_{Guid.NewGuid():N}";
            AddTagRename(toTemp, oldTag, temp);
            AddTagRename(toFinal, temp, newTag);
        }

        return (toTemp, toFinal);
    }

    internal static void AddTagRename(Dictionary<string, string?> replacements, string oldTag, string newTag)
    {
        replacements[$"{{{oldTag}}}"] = $"{{{newTag}}}";
        replacements[$"{{{oldTag}."] = $"{{{newTag}.";
    }
}

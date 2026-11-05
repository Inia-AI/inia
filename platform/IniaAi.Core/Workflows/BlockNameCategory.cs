namespace IniaAi.Core.Workflows;

/// <summary>
/// Renders "N block(s) &lt;label&gt;: a, b, c." sentences for run-failure messages, capping the
/// names so a wide workflow cannot turn one failure into an unbounded string.
/// </summary>
internal static class BlockNameCategory
{
    private const int MAX_NAMES = 10;

    internal static void AppendTo(List<string> parts, string label, IReadOnlyList<string> blockNames)
    {
        if (blockNames.Count == 0)
        {
            return;
        }

        string names = string.Join(", ", blockNames.Take(MAX_NAMES));
        string suffix = blockNames.Count > MAX_NAMES
            ? $", +{blockNames.Count - MAX_NAMES} more"
            : string.Empty;

        parts.Add($"{blockNames.Count} block(s) {label}: {names}{suffix}.");
    }
}

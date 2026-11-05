namespace IniaAi.WebApp.Components.UserWorkflows.Common;

/// <summary>
/// The executed-workflows list's free-text search. Covers the columns the grid shows, including
/// the execution's state and, for a failed run, its kind and detail — so triage can find a run by
/// what went wrong, not only by name.
/// </summary>
public static class ExecutedWorkflowRowFilter
{
    private const string CREATED_AT_FORMAT = "yyyy-MM-dd HH:mm";

    /// <summary>Rows matching <paramref name="searchTerm"/>; every row when it is blank.</summary>
    public static IEnumerable<ExecutedWorkflowRow> Apply(
        IEnumerable<ExecutedWorkflowRow> rows,
        string? searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return rows;
        }

        string term = searchTerm.Trim();

        return rows.Where(row => Matches(row, term));
    }

    private static bool Matches(ExecutedWorkflowRow row, string term)
    {
        return Contains(row.CreatedAt.ToString(CREATED_AT_FORMAT), term)
            || Contains(row.Name, term)
            || Contains(row.TypeName, term)
            || Contains(row.Description, term)
            || Contains(ExecutionOutcomeVisuals.Label(row.State), term)
            || Contains(row.FailureKind, term)
            || Contains(row.FailureDetail, term)
            || Contains(row.WarningKind, term)
            || Contains(row.WarningDetail, term);
    }

    private static bool Contains(string? candidate, string term)
    {
        return !string.IsNullOrEmpty(candidate)
            && candidate.Contains(term, StringComparison.OrdinalIgnoreCase);
    }
}

namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents configuration details for a column in a table output.
/// Used to define how each column should be labeled, formatted, and grouped.
/// </summary>
public class TableConfigDto
{
    /// <summary>
    /// Gets or sets the header label for the column.
    /// This is typically the display name shown in the table's header row.
    /// </summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the format string used to display the column values.
    /// Examples include date formats (e.g., "MM/dd/yyyy"), numeric formats, etc.
    /// </summary>
    public string Format { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the optional name of the group to which this column belongs.
    /// Useful for visual grouping in hierarchical or multi-header tables.
    /// </summary>
    public string? GroupName { get; set; } = string.Empty;
}

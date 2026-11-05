namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents a table output generated as part of a workflow block's result.
/// Includes metadata, configuration, and the data to be rendered.
/// </summary>
public class TableOutputDto : WorkflowOutputDataDto
{
    /// <summary>
    /// Identifies this as table output data.
    /// </summary>
    public override string OutputType => "Table";

    /// <summary>
    /// Gets or sets the name or title of the table.
    /// Used for display or identification purposes.
    /// Legacy property for backward compatibility - maps to base Name property.
    /// </summary>
    public string TableName
    {
        get => Name;
        set => Name = value;
    }

    /// <summary>
    /// Gets or sets the configuration details of the table columns.
    /// Defines headers, formatting, column types, etc.
    /// </summary>
    public List<TableConfigDto> TableConfig { get; set; } = [];

    /// <summary>
    /// Gets or sets the actual data to be displayed in the table.
    /// Each inner list represents a row, with strings corresponding to column values.
    /// </summary>
    public List<List<string>> TableData { get; set; } = [];
}

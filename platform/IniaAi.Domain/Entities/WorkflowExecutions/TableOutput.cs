using System.ComponentModel.DataAnnotations.Schema;

namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Represents table output data for workflow elements
/// </summary>
public class TableOutput : WorkflowOutputData
{
    /// <summary>
    /// Identifies this as table output data
    /// </summary>
    public override string OutputType => "Table";

    /// <summary>
    /// Legacy property for backward compatibility - maps to base Name property
    /// </summary>
    public string TableName
    {
        get => Name;
        set => Name = value;
    }

    [NotMapped]
    public List<TableConfig> TableConfig { get; set; } = [];

    public List<List<string>> TableData { get; set; } = [];
}

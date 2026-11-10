namespace IniaAi.Domain.Entities.WorkflowExecutions;

/// <summary>
/// Represents grid layout configuration for positioning workflow output elements
/// </summary>
public class GridLayoutOutput : WorkflowOutputData
{
    public const string GRID_MODE = "grid";
    public const int DEFAULT_COLUMNS = 2;

    /// <summary>
    /// Identifies this as grid layout configuration data
    /// </summary>
    public override string OutputType => "GridLayout";

    /// <summary>
    /// The layout mode - "sequential" or "grid"
    /// </summary>
    public string Mode { get; set; } = "sequential";

    /// <summary>
    /// The number of columns in the grid
    /// </summary>
    public int Columns { get; set; } = DEFAULT_COLUMNS;

    /// <summary>
    /// The positioning configuration for each child element
    /// </summary>
    public List<GridElementPosition> Elements { get; set; } = [];
}

/// <summary>
/// Represents the grid position of a single element
/// </summary>
public class GridElementPosition
{
    /// <summary>
    /// The order of the block being positioned
    /// </summary>
    public int BlockOrder { get; set; } = 1;

    /// <summary>
    /// The starting row position (1-based)
    /// </summary>
    public int Row { get; set; } = 1;

    /// <summary>
    /// The starting column position (1-based)
    /// </summary>
    public int Column { get; set; } = 1;

    /// <summary>
    /// The number of rows this element spans
    /// </summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>
    /// The number of columns this element spans
    /// </summary>
    public int ColumnSpan { get; set; } = 1;
}

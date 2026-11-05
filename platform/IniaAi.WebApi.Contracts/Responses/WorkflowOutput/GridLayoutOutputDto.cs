namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Represents a grid layout configuration for workflow output elements.
/// Defines how elements should be positioned in a CSS Grid layout.
/// </summary>
public class GridLayoutOutputDto : WorkflowOutputDataDto
{
    /// <summary>
    /// Identifies this as grid layout output data.
    /// </summary>
    public override string OutputType => "GridLayout";

    /// <summary>
    /// Gets or sets the layout mode (grid or sequential).
    /// </summary>
    public string Mode { get; set; } = "sequential";

    /// <summary>
    /// Gets or sets the number of columns in the grid.
    /// </summary>
    public int Columns { get; set; } = 2;

    /// <summary>
    /// Gets or sets the positioning information for each element in the grid.
    /// </summary>
    public List<GridElementPositionDto> Elements { get; set; } = [];
}

/// <summary>
/// Represents the position and span of an element in the grid layout.
/// </summary>
public class GridElementPositionDto
{
    /// <summary>
    /// Gets or sets the block order used to map to workflow output elements.
    /// </summary>
    public int BlockOrder { get; set; }

    /// <summary>
    /// Gets or sets the grid row position (1-based).
    /// </summary>
    public int Row { get; set; }

    /// <summary>
    /// Gets or sets the grid column position (1-based).
    /// </summary>
    public int Column { get; set; }

    /// <summary>
    /// Gets or sets the number of rows this element spans.
    /// </summary>
    public int RowSpan { get; set; } = 1;

    /// <summary>
    /// Gets or sets the number of columns this element spans.
    /// </summary>
    public int ColumnSpan { get; set; } = 1;
}
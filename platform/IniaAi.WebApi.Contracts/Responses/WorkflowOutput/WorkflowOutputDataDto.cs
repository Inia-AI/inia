using System.Text.Json.Serialization;

namespace IniaAi.WebApi.Contracts.Responses.WorkflowOutput;

/// <summary>
/// Base class for all workflow output data transfer objects.
/// Provides polymorphic support for different types of output data (tables, charts, etc.).
/// </summary>
[JsonDerivedType(typeof(TableOutputDto), typeDiscriminator: "Table")]
[JsonDerivedType(typeof(ChartOutputDto), typeDiscriminator: "Chart")]
[JsonDerivedType(typeof(GridLayoutOutputDto), typeDiscriminator: "GridLayout")]
public abstract class WorkflowOutputDataDto
{
    /// <summary>
    /// Gets or sets the display name/title of the output data.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets the type discriminator for this output data.
    /// Used by the frontend to determine how to render the data.
    /// </summary>
    public abstract string OutputType { get; }
}
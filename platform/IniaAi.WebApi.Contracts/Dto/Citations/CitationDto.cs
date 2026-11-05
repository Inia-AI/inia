namespace IniaAi.WebApi.Contracts.Dto.Citations;

/// <summary>
/// Represents a citation with its sources.
/// Links to fact markers in content via the Id property.
/// </summary>
public class CitationDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the citation.
    /// Corresponds to the id attribute in &lt;fact id="..."&gt; markers in content.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the collection of sources for this citation.
    /// </summary>
    public List<CitationSourceDto> Sources { get; set; } = [];

    /// <summary>
    /// Gets or sets whether this citation references numerical data.
    /// Numerical citations are linked to &lt;fact&gt; tags in content.
    /// Non-numerical (broad) citations are included in the array but not linked to content markers.
    /// </summary>
    public bool IsNumerical { get; set; }
}

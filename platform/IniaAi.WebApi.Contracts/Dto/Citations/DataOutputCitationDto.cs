namespace IniaAi.WebApi.Contracts.Dto.Citations;

/// <summary>
/// A citation of one figure inside a data output's JSON.
/// </summary>
public class DataOutputCitationDto : CitationDto
{
    /// <summary>
    /// Gets or sets the JSON Pointer (RFC 6901) of the value that holds the cited figure, for
    /// example <c>/facilities/0/limit</c>.
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the cited figure as it is written in that value. A text value can hold several.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}

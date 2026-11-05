using System.ComponentModel.DataAnnotations;

namespace IniaAi.WebApi.Contracts.Responses.Graph;

/// <summary>
/// Response containing AI-generated answer to the user's question about workflow provenance.
/// </summary>
public class GraphQueryResponse
{
    /// <summary>
    /// Gets or sets the AI-generated answer to the user's question, grounded in provenance data.
    /// </summary>
    [Required]
    public string Answer { get; set; } = string.Empty;
}

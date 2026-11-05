using System.ComponentModel.DataAnnotations;

namespace IniaAi.WebApi.Contracts.Requests.CounterpartyTypes;

/// <summary>
/// Request to create a counterparty type, or to replace the name and description of one.
/// </summary>
public record CounterpartyTypeRequest
{
    /// <summary>
    /// Gets the name of the counterparty type. Names are unique, ignoring case.
    /// </summary>
    [Required]
    [StringLength(250, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the description of the counterparty type.
    /// </summary>
    [StringLength(1000)]
    public string? Description { get; init; }
}

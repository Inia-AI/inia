namespace IniaAi.WebApi.Contracts.Responses.Counterparty;

/// <summary>
/// Represents a counterparty entity with identification, hierarchy, and naming information.
/// </summary>
public class CounterpartyListItemDto
{
    /// <summary>
    /// Gets or sets the unique identifier of the counterparty.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the parent counterparty, if any.
    /// Used to model hierarchical relationships (e.g., subsidiaries).
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the identifiers representing the types/categories of the counterparty.
    /// </summary>
    public List<string> CounterpartyTypeIds { get; set; } = [];

    /// <summary>
    /// Gets or sets the display name of the counterparty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets alternative name variants or aliases for the counterparty.
    /// Typically used to improve searchability and data matching.
    /// </summary>
    public string NameVariants { get; set; } = string.Empty;
}

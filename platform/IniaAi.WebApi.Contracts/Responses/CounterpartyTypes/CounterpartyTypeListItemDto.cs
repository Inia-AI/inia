namespace IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;

/// <summary>
/// Data Transfer Object for a Counterparty Type.
/// </summary>
public class CounterpartyTypeListItemDto
{
    /// <summary>
    /// The unique identifier of the counterparty type.
    /// </summary>
    public string Id { get; set; } = default!;

    /// <summary>
    /// The name of the counterparty type.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// The description of the counterparty type.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The number of the caller's organization's counterparties that carry this type.
    /// </summary>
    public int CounterpartyCount { get; set; }

    /// <summary>
    /// Whether the platform relies on this type by name. A built-in type cannot be renamed or deleted.
    /// </summary>
    public bool IsBuiltIn { get; set; }
}

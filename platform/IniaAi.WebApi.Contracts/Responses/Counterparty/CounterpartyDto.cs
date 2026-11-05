using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.WebApi.Contracts.Responses.Counterparty;

/// <summary>
/// Represents a counterparty entity with identification, hierarchy, and naming information.
/// </summary>
public class CounterpartyDto : ICounterpartyFields
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

    /// <summary>
    /// Gets or sets the email address associated with the counterparty.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the phone number associated with the counterparty.
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Gets or sets the website URL of the counterparty.
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// Gets or sets the address of the counterparty.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the Last Financial Analysis run of the counterparty (JSON).
    /// </summary>
    public string? LastFinancialAnalysisResult { get; set; }

    /// <summary>
    /// Gets or sets the freeform property bag — author-coined <c>key → value</c> pairs workflows read via
    /// <c>{Counterparty.key}</c> / <c>{Funds.key}</c> placeholders, or <c>null</c> when the bag is empty.
    /// </summary>
    public Dictionary<string, string>? Properties { get; set; }
}

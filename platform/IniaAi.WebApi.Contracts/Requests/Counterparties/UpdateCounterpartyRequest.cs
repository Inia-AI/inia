namespace IniaAi.WebApi.Contracts.Requests.Counterparties;

/// <summary>
/// Represents the request payload to update an existing counterparty.
/// </summary>
public class UpdateCounterpartyRequest
{
    /// <summary>
    /// Gets or sets the parent counterparty ID (optional).
    /// </summary>
    public string? ParentId { get; set; }

    /// <summary>
    /// Gets or sets the type/category IDs of the counterparty.
    /// </summary>
    public List<string>? CounterpartyTypeIds { get; set; }

    /// <summary>
    /// Gets or sets the display name of the counterparty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the alternative name variants or aliases.
    /// </summary>
    public string NameVariants { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the email address of the counterparty.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Gets or sets the phone number of the counterparty.
    /// </summary>
    public string? PhoneNumber { get; set; }

    /// <summary>
    /// Gets or sets the website URL of the counterparty.
    /// </summary>
    public string? Website { get; set; }

    /// <summary>
    /// Gets or sets the physical address of the counterparty.
    /// </summary>
    public string? Address { get; set; }

    /// <summary>
    /// Gets or sets the result of the last financial analysis.
    /// </summary>
    public string? LastFinancialAnalysisResult { get; set; }

    /// <summary>
    /// Gets or sets the freeform property bag — author-coined <c>key → value</c> pairs a workflow can
    /// pull in via <c>{Counterparty.key}</c> / <c>{Funds.key}</c> placeholders. Omitting it (or sending
    /// <c>null</c>) leaves the stored bag unchanged; a supplied bag replaces it wholesale (empty clears it).
    /// </summary>
    public Dictionary<string, string>? Properties { get; set; }
}

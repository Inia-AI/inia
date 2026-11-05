namespace IniaAi.WebApi.Contracts.Dto;

/// <summary>
/// The fields every counterparty write carries, whichever surface the write arrives through: the
/// create and update commands, and each row of a counterparty bundle. The counterparty read model
/// carries them back. One shape, so the field rules are declared once and cannot drift between the
/// API, the bundle import and the export.
/// </summary>
public interface ICounterpartyFields
{
    /// <summary>The display name.</summary>
    string Name { get; }

    /// <summary>Pipe-separated name variants.</summary>
    string NameVariants { get; }

    /// <summary>The contact email address.</summary>
    string? Email { get; }

    /// <summary>The contact phone number.</summary>
    string? PhoneNumber { get; }

    /// <summary>The website, an absolute http or https URL.</summary>
    string? Website { get; }

    /// <summary>The postal address.</summary>
    string? Address { get; }

    /// <summary>The stored result of the latest financial analysis.</summary>
    string? LastFinancialAnalysisResult { get; }

    /// <summary>The freeform property bag.</summary>
    Dictionary<string, string>? Properties { get; }
}

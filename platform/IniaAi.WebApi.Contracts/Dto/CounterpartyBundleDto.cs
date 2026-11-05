namespace IniaAi.WebApi.Contracts.Dto;

/// <summary>
/// An organization's counterparties as one portable file: every active counterparty with its
/// hierarchy, its types by name, its property bag and its stored financial analysis, plus the
/// type definitions the importer creates when the target deployment lacks them. Ids travel and
/// are kept on import: executions, reports and baseline registrations reference a counterparty
/// by id, and a customer's instances are expected to agree on those ids. Document links and user
/// links do not travel; they point at rows the target deployment does not have.
/// </summary>
public class CounterpartyBundleDto
{
    /// <summary>The bundle format this exporter writes and the importer accepts.</summary>
    public const int CURRENT_FORMAT_VERSION = 1;

    /// <summary>Version of the bundle format; imports refuse a version they do not know.</summary>
    public int BundleFormatVersion { get; set; } = CURRENT_FORMAT_VERSION;

    /// <summary>The deployment the bundle was exported from (the exporting API's host).</summary>
    public string? SourceInstance { get; set; }

    /// <summary>The organization the bundle was exported from, by the exporting deployment's id; provenance only.</summary>
    public string? SourceOrganizationId { get; set; }

    /// <summary>The exporting organization's name; provenance and the download's file name.</summary>
    public string? SourceOrganizationName { get; set; }

    /// <summary>When the bundle was exported, UTC.</summary>
    public DateTime ExportedAt { get; set; }

    /// <summary>The counterparty types the bundled counterparties use, so a target that lacks one can create it.</summary>
    public List<CounterpartyBundleTypeDto> CounterpartyTypes { get; set; } = [];

    /// <summary>The counterparties, as the export ordered them (by name, then id); the importer lands parents before children whatever the order.</summary>
    public List<CounterpartyBundleEntryDto> Counterparties { get; set; } = [];
}

/// <summary>One counterparty type definition; types are deployment-wide and match by name.</summary>
public class CounterpartyBundleTypeDto
{
    /// <summary>The type's name, the matching key across deployments.</summary>
    public required string Name { get; set; }

    /// <summary>The type's description, used when the importer creates the type.</summary>
    public string? Description { get; set; }
}

/// <summary>One counterparty of the bundle.</summary>
public class CounterpartyBundleEntryDto : ICounterpartyFields
{
    /// <summary>The counterparty's id on the exporting deployment; kept on import.</summary>
    public required string Id { get; set; }

    /// <summary>The parent counterparty's id within this bundle; null for a root.</summary>
    public string? ParentId { get; set; }

    /// <summary>The display name; the fallback matching key when the id is unknown on the target.</summary>
    public required string Name { get; set; }

    /// <summary>Pipe-separated name variants.</summary>
    public string NameVariants { get; set; } = string.Empty;

    /// <summary>The contact email address.</summary>
    public string? Email { get; set; }

    /// <summary>The contact phone number.</summary>
    public string? PhoneNumber { get; set; }

    /// <summary>The website, an absolute http or https URL.</summary>
    public string? Website { get; set; }

    /// <summary>The postal address.</summary>
    public string? Address { get; set; }

    /// <summary>The stored result of the latest financial analysis, as written by workflow runs.</summary>
    public string? LastFinancialAnalysisResult { get; set; }

    /// <summary>The freeform property bag; null when the counterparty has none.</summary>
    public Dictionary<string, string>? Properties { get; set; }

    /// <summary>The counterparty's types by name; each is found or created on the target.</summary>
    public List<string> CounterpartyTypeNames { get; set; } = [];
}

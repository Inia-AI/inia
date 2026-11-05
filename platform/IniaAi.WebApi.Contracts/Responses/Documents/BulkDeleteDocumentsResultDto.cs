namespace IniaAi.WebApi.Contracts.Responses.Documents;

/// <summary>
/// Result of a bulk delete operation. The handler processes each requested ID
/// independently and reports per-id success or failure so clients can surface
/// partial outcomes without needing to retry the whole batch.
/// </summary>
public sealed class BulkDeleteDocumentsResultDto
{
    /// <summary>Gets or sets the IDs that were successfully deleted.</summary>
    public List<string> Deleted { get; set; } = [];

    /// <summary>Gets or sets the per-id failure reasons keyed by document ID.</summary>
    public Dictionary<string, string> Failed { get; set; } = [];
}

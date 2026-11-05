namespace IniaAi.WebApi.Contracts.Requests.WorkflowTemplates;

/// <summary>
/// Body for renaming a document class. The class addressed by the path key is renamed to
/// <see cref="NewKey"/>, cascading the new key into every block and parameterized-section reference.
/// </summary>
/// <param name="NewKey">The class's new key.</param>
/// <param name="BaseVersionId">Head version id for optimistic concurrency; a stale or missing value is rejected with 409 Conflict.</param>
public record RenameDocumentClassRequest(string NewKey, string? BaseVersionId = null);

namespace IniaAi.WebApi.Contracts.Requests.Documents;

/// <summary>
/// Request body for deleting many documents in a single call. Each ID is processed
/// independently — a failure on one does not abort the rest; the response reports
/// per-id success/failure (see <c>BulkDeleteDocumentsResultDto</c>).
/// </summary>
public sealed record BulkDeleteDocumentsRequest(List<string> Ids);

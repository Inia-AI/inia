namespace IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

/// <summary>
/// One workflow's current saved version on a deployment, as carried in a currency card file.
/// <see cref="SavedByName"/> is the author's display name, resolved on the deployment that
/// generated the card so other deployments can show it without that user in their own data.
/// <see cref="HistoryHashes"/> is this workflow's full lineage of canonical content hashes on the
/// deployment, oldest first and ending at <see cref="ContentHash"/>; comparing two deployments'
/// lineages is what lets the grid tell "behind" apart from "diverged".
/// <see cref="WorkflowTypeName"/> is the workflow type's name on the generating deployment, the
/// label the rest of the app shows; a card written before the field existed carries none.
/// </summary>
public record CurrencyCardEntryDto(
    string WorkflowTypeId,
    string VersionId,
    string ContentHash,
    string ShortCode,
    DateTime SavedUtc,
    string SavedByUserId,
    string SavedByName,
    IReadOnlyList<string> HistoryHashes,
    string? WorkflowTypeName = null);

/// <summary>
/// A per-deployment manifest of the current saved version of every workflow. Generated on one
/// deployment, carried as a JSON file to another, and dropped into the compare grid to answer
/// "which deployment has the latest". This record's JSON IS the card file format.
/// </summary>
public record CurrencyCardDto(
    string InstanceName,
    DateTime GeneratedUtc,
    IReadOnlyList<CurrencyCardEntryDto> Workflows);

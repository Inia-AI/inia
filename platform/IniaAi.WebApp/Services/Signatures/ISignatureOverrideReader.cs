namespace IniaAi.WebApp.Services.Signatures;

/// <summary>
/// Read-only accessor for <see cref="Domain.Entities.Signatures.SignatureFieldOverride"/>
/// rows from the staff editor. Overrides are client-owned but staff still want to peek
/// at the active value inline with the field definition; this service exists so editor
/// components don't talk to the DbContext directly.
/// </summary>
public interface ISignatureOverrideReader
{
    /// <summary>
    /// Loads every active override scoped to <paramref name="organizationId"/> +
    /// <paramref name="workflowTypeId"/>, keyed by field name (overrides are
    /// workflow-level; one value applies to every block referencing the field).
    /// Returns an empty dictionary when no rows exist or the workflow type is missing.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> LoadForWorkflowAsync(
        string organizationId,
        string? workflowTypeId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Counts active and pending override rows for one field across ALL organizations —
    /// powers the delete-confirmation dialog ("N client override(s) will be deleted").
    /// Uncached: deletes are rare and the count must be fresh.
    /// </summary>
    Task<(int Active, int Pending)> CountOverridesForFieldAsync(
        string? workflowTypeId,
        string fieldName,
        CancellationToken cancellationToken);
}

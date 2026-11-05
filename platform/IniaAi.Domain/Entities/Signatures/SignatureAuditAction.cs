namespace IniaAi.Domain.Entities.Signatures;

/// <summary>
/// String constants for <see cref="SignatureFieldOverrideAuditLog.Action"/>. Kept as
/// constants rather than an enum so the column persists as a stable text value across
/// providers and remains human-readable in SQL.
/// </summary>
public static class SignatureAuditAction
{
    public const string CREATED = "Created";
    public const string UPDATED = "Updated";
    public const string REMOVED = "Removed";

    /// <summary>
    /// A staff-side field rename carried the override to a new
    /// <c>PlaceholderName</c>; <c>PreviousValue</c>/<c>NewValue</c> hold the unchanged
    /// override value, the row's <c>PlaceholderName</c> holds the new name.
    /// </summary>
    public const string RENAMED = "Renamed";
}

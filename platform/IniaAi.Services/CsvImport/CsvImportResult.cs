namespace IniaAi.Services.CsvImport;

/// <summary>
/// Represents the result of a CSV import operation, including any validation errors.
/// </summary>
public class CsvImportResult
{
    /// <summary>
    /// Number of new records successfully created.
    /// </summary>
    public int ImportedCount { get; set; }

    /// <summary>
    /// Number of existing records that were updated.
    /// </summary>
    public int UpdatedCount { get; set; }

    /// <summary>
    /// Number of records that were skipped.
    /// </summary>
    public int SkippedCount { get; set; }

    /// <summary>
    /// List of validation or import errors encountered.
    /// </summary>
    public List<CsvImportError> Errors { get; set; } = [];

    /// <summary>
    /// Indicates whether any errors occurred during import.
    /// </summary>
    public bool HasErrors => Errors.Count != 0;

    /// <summary>
    /// Total number of rows processed.
    /// </summary>
    public int TotalProcessed => ImportedCount + UpdatedCount + SkippedCount;

    /// <summary>
    /// Creates a result indicating successful import with no updates.
    /// Used when all records are either imported or skipped, with no existing records updated.
    /// </summary>
    public CsvImportResult(int importedCount, int skippedCount)
    {
        ImportedCount = importedCount;
        SkippedCount = skippedCount;
        UpdatedCount = 0;
    }

    /// <summary>
    /// Creates a result indicating successful import with updates.
    /// </summary>
    public CsvImportResult(int importedCount, int updatedCount, int skippedCount)
    {
        ImportedCount = importedCount;
        UpdatedCount = updatedCount;
        SkippedCount = skippedCount;
    }

    /// <summary>
    /// Creates a result with validation errors.
    /// </summary>
    public CsvImportResult(List<CsvImportError> errors)
    {
        Errors = errors ?? [];
        SkippedCount = 0; // When there are validation errors, no records are processed
        ImportedCount = 0;
        UpdatedCount = 0;
    }

    /// <summary>
    /// Creates an empty result.
    /// </summary>
    public CsvImportResult()
    {
    }
}

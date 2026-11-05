namespace IniaAi.Services.CsvImport;

/// <summary>
/// Represents an error encountered during CSV import validation or processing.
/// </summary>
public class CsvImportError
{
    /// <summary>
    /// The row number in the CSV file where the error occurred (1-based, excluding header).
    /// </summary>
    public int RowNumber { get; set; }

    /// <summary>
    /// The ID of the entity that caused the error, if applicable.
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// The name of the entity that caused the error.
    /// </summary>
    public string? EntityName { get; set; }

    /// <summary>
    /// The type of error that occurred.
    /// </summary>
    public CsvImportErrorType ErrorType { get; set; }

    /// <summary>
    /// A detailed error message describing what went wrong.
    /// </summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>
    /// A suggested action for the user to resolve the error.
    /// </summary>
    public string SuggestedAction { get; set; } = string.Empty;

    /// <summary>
    /// Additional details about the conflict or error.
    /// </summary>
    public Dictionary<string, string> ConflictDetails { get; set; } = [];

    /// <summary>
    /// The raw CSV row data that caused the error.
    /// </summary>
    public string? RawRowData { get; set; }
}

/// <summary>
/// Types of errors that can occur during CSV import.
/// </summary>
public enum CsvImportErrorType
{
    /// <summary>
    /// A name already exists as an active record.
    /// </summary>
    NameConflict,

    /// <summary>
    /// The same name appears multiple times within the CSV file.
    /// </summary>
    DuplicateInCsv,

    /// <summary>
    /// An ID was not found in the database.
    /// </summary>
    IdNotFound,

    /// <summary>
    /// A required field is missing or invalid.
    /// </summary>
    ValidationError,

    /// <summary>
    /// A foreign key reference is invalid.
    /// </summary>
    InvalidReference,

    /// <summary>
    /// Data format or type conversion error.
    /// </summary>
    DataFormatError,

    /// <summary>
    /// General or unknown error type.
    /// </summary>
    Unknown
}
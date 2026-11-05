namespace IniaAi.Domain.Entities.Documents;

/// <summary>
/// Why a file's bytes were refused at intake: by the deterministic inspector, or by the
/// organization's upload limits. Each value fits a 4xx body and the reason column of whatever
/// records the refusal.
/// </summary>
public static class IntakeRefusalReasons
{
    /// <summary>The bytes are not a document type the platform admits, or not the format family the filename claims.</summary>
    public const string FILE_TYPE_MISMATCH = "FileTypeMismatch";

    /// <summary>The file has no bytes.</summary>
    public const string EMPTY_FILE = "EmptyFile";

    /// <summary>The file is larger than the deployment's upload limit.</summary>
    public const string FILE_TOO_LARGE = "FileTooLarge";

    /// <summary>The file declares more content than the platform will expand: archive entries, expanded size, compression ratio, image pixels.</summary>
    public const string STRUCTURAL_LIMIT = "StructuralLimit";

    /// <summary>The file starts like a known container but the container cannot be read.</summary>
    public const string MALFORMED_FILE = "MalformedFile";

    /// <summary>The file is a format the platform accepts, but this build has no reader for it.</summary>
    public const string FORMAT_NOT_READ = "FormatNotRead";

    /// <summary>Storing the file would take the organization past its storage quota.</summary>
    public const string STORAGE_FULL = "StorageFull";

    /// <summary>The organization has uploaded as many files as it may in the last hour.</summary>
    public const string TOO_MANY_UPLOADS = "TooManyUploads";

    /// <summary>
    /// True for a refusal that protects the platform itself rather than the workflow the file is
    /// for: a file over the upload limit, or one that expands past what the platform will hold.
    /// These stand even when the sender vouches for the file.
    /// </summary>
    public static bool ProtectsThePlatform(string reason)
    {
        return reason is FILE_TOO_LARGE or STRUCTURAL_LIMIT;
    }
}

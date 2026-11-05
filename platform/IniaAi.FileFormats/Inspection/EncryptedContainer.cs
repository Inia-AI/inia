namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// Which encrypted container the inspector found, so the caller can route to unlock without
/// reading the filename. An Office package wraps its document in a compound file, and the
/// document type inside is known only after unlock.
/// </summary>
public enum EncryptedContainer
{
    Pdf,
    LegacyWorkbook,
    OfficePackage,
}

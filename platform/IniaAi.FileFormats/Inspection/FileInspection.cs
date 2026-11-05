using IniaAi.Common.Enums;

namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// The verdict of <see cref="FileContentInspector"/>. A refusal carries a
/// <see cref="Domain.Entities.Documents.IntakeRefusalReasons"/> value in <paramref name="ReasonCode"/>
/// and a one-sentence <paramref name="Detail"/> fit for a 4xx body. An encrypted verdict is not a
/// refusal: <paramref name="Container"/> names what the caller hands to unlock, and the decrypted
/// copy is inspected again. <paramref name="Detected"/> is null when the sequence stopped before
/// detection ran.
/// </summary>
public sealed record FileInspection(
    FileInspectionOutcome Outcome,
    string? ReasonCode,
    string? Detail,
    DetectedContent? Detected,
    EncryptedContainer? Container)
{
    public string? DetectedMediaType => Detected?.IanaMediaType;

    public MediaType? MediaType => Detected?.PlatformMediaType;

    public bool HasMacros => Detected?.HasMacros ?? false;

    public static FileInspection Accept(DetectedContent? detected)
    {
        return new FileInspection(FileInspectionOutcome.Accepted, null, null, detected, null);
    }

    public const string DAMAGED_FILE = "The file is damaged and cannot be opened. Save or download it again, then upload the new copy.";

    public static FileInspection Refuse(string reasonCode, string detail, DetectedContent? detected)
    {
        return new FileInspection(FileInspectionOutcome.Refused, reasonCode, detail, detected, null);
    }

    public static FileInspection NeedsUnlock(EncryptedContainer container, string detail, DetectedContent detected)
    {
        return new FileInspection(FileInspectionOutcome.Encrypted, null, detail, detected, container);
    }
}

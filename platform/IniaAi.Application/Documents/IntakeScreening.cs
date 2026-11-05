using IniaAi.Common.Enums;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Unlock;

namespace IniaAi.Application.Documents;

/// <summary>
/// What <see cref="DocumentIntakeGate"/> decided about one file. On <see cref="IntakeOutcome.Admitted"/>,
/// <paramref name="Bytes"/> is the copy to store (the decrypted copy when the file was unlocked)
/// and <paramref name="Detected"/> names its type; on every other outcome <paramref name="Bytes"/>
/// is the copy the gate screened, and empty when the file was refused from its declared size
/// before any byte was read. <paramref name="Unlock"/> is set when the unlock gate ran.
/// <paramref name="ReasonCode"/> and <paramref name="Detail"/> are set on a refusal.
/// </summary>
public sealed record IntakeScreening(
    IntakeOutcome Outcome,
    byte[] Bytes,
    DetectedContent? Detected,
    FileUnlockResult? Unlock,
    string? ReasonCode,
    string? Detail)
{
    /// <summary>The platform type of an admitted file. Admission guarantees the inspector named one.</summary>
    public MediaType MediaType => Outcome is IntakeOutcome.Admitted && Detected?.PlatformMediaType is { } mediaType
        ? mediaType
        : throw new InvalidOperationException($"Only an admitted file has a media type; this one is {Outcome}.");

    /// <summary>True when an admitted file carries macros; the mouth stamps it on the stored document.</summary>
    public bool HasMacros => Outcome is IntakeOutcome.Admitted && Detected is { HasMacros: true };

    /// <summary>The IANA name of an admitted file's type, for the stored blob's content type.</summary>
    public string ContentType => Outcome is IntakeOutcome.Admitted && Detected is { } detected
        ? detected.IanaMediaType
        : throw new InvalidOperationException($"Only an admitted file has a content type; this one is {Outcome}.");
}

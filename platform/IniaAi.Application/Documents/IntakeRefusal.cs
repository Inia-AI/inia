namespace IniaAi.Application.Documents;

/// <summary>
/// One refusal by the deterministic inspector: the bytes that were judged, the name they came
/// with, the <see cref="Domain.Entities.Documents.IntakeRefusalReasons"/> value, and the type the
/// inspector detected when it got that far.
/// </summary>
public sealed record IntakeRefusal(
    string FileName,
    byte[]? ScreenedBytes,
    long SizeBytes,
    string ReasonCode,
    string? DetectedMediaType)
{
    public static IntakeRefusal OfBytes(string fileName, byte[] screenedBytes, string reasonCode, string? detectedMediaType)
    {
        return new IntakeRefusal(fileName, screenedBytes, screenedBytes.Length, reasonCode, detectedMediaType);
    }

    public static IntakeRefusal Unread(string fileName, long sizeBytes, string reasonCode)
    {
        return new IntakeRefusal(fileName, null, sizeBytes, reasonCode, null);
    }
}

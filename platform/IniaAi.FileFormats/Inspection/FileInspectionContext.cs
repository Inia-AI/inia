namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// The bytes under inspection plus what earlier checks in the sequence learned about them.
/// </summary>
public sealed class FileInspectionContext
{
    public FileInspectionContext(byte[] bytes, string fileName, FileInspectionLimits limits)
    {
        Bytes = bytes;
        FileName = fileName;
        Limits = limits;
    }

    public byte[] Bytes { get; }

    public string FileName { get; }

    public FileInspectionLimits Limits { get; }

    /// <summary>Entry names of a zip container, recorded by the archive check once the directory passed its limits.</summary>
    public IReadOnlyList<string>? ArchiveEntries { get; set; }

    /// <summary>Set by the detection check. Null means detection has not run yet, never "unrecognised".</summary>
    public DetectedContent? Detected { get; set; }

    public DetectedContent RequireDetected()
    {
        return Detected ?? throw new InvalidOperationException("This check reads the detected type, so it must run after ContentTypeDetectionCheck.");
    }

    public bool StartsWith(ReadOnlySpan<byte> signature)
    {
        return Bytes.AsSpan().StartsWith(signature);
    }

    public MemoryStream OpenStream()
    {
        return new MemoryStream(Bytes, writable: false);
    }
}

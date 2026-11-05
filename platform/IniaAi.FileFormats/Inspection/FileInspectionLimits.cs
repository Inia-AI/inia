using IniaAi.Configuration.Platform;

namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// The ceilings the inspector enforces before anything expands the file. The file size is the
/// deployment's upload limit and comes from <see cref="IniaAiSettings"/>, so it has no default of
/// its own here. The structural figures are read from the zip central directory and the image
/// header, so they cost nothing proportional to the content; the ratio only applies above
/// <see cref="CompressionRatioFloorBytes"/>, because a small Office file made of near-empty XML
/// compresses far better than any bomb needs to. The package manifest has its own ceiling
/// because it is the one entry the signature library materialises to tell a macro-enabled
/// package from a plain one.
/// </summary>
public sealed record FileInspectionLimits
{
    private const long BYTES_PER_MEGABYTE = 1024 * 1024;

    public static readonly FileInspectionLimits Default = FromSettings(new IniaAiSettings());

    public required long MaxFileBytes { get; init; }

    public int MaxArchiveEntries { get; init; } = 10_000;

    public long MaxExpandedBytes { get; init; } = 1L << 30;

    public int MaxCompressionRatio { get; init; } = 100;

    public long CompressionRatioFloorBytes { get; init; } = 10L * BYTES_PER_MEGABYTE;

    public long MaxPackageManifestBytes { get; init; } = BYTES_PER_MEGABYTE;

    public long MaxImagePixels { get; init; } = 100_000_000;

    public static FileInspectionLimits FromSettings(IniaAiSettings settings)
    {
        return new FileInspectionLimits { MaxFileBytes = settings.MaxFileSizeMb * BYTES_PER_MEGABYTE };
    }
}

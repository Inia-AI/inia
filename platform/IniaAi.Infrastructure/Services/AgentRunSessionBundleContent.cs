using IniaAi.Common.Interfaces.Services.AgentRuntime;
using System.IO.Compression;
using System.Text.Json;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Reads what the platform needs out of a session bundle: a gzipped JSON manifest whose bulk is
/// the base64 of every file it carries, of which custody records only the version stamp. The
/// session itself is never unpacked — no file is decoded, written or opened.
/// </summary>
public static class AgentRunSessionBundleContent
{
    /// <summary>
    /// The bound on what this will decompress — the shared mirror of the runtime's
    /// <c>MAX_CHECKPOINT_BUNDLE_BYTES</c>, aliased rather than re-declared so custody and the
    /// continuation dispatch can never admit at different bounds. A bundle is compressed on the
    /// wire, so without a bound a malformed or hostile payload would decide how much memory the
    /// reader takes.
    /// </summary>
    public const long MAX_BUNDLE_BYTES = AgentRunContinuationCapacity.MAX_SESSION_BUNDLE_BYTES;

    /// <summary>
    /// The column the stamp is recorded in. A stamp past this is not truncated — a truncated
    /// stamp reads as a different build and would make every later comparison lie — it is
    /// treated as no stamp at all.
    /// </summary>
    public const int MAX_VERSION_STAMP_CHARS = 1024;

    private const string VERSION_PROPERTY = "version";

    /// <summary>
    /// Sizes the inflate buffer up front so a typical bundle does not grow it by doubling.
    /// Deliberately a guess at gzip's ratio on base64 text, not a bound — the bound is
    /// <see cref="MAX_BUNDLE_BYTES"/>, checked as the bytes arrive.
    /// </summary>
    private const int INFLATED_SIZE_GUESS = 8;

    private const int COPY_BUFFER_BYTES = 81920;
    private const byte GZIP_MAGIC_FIRST = 0x1f;
    private const byte GZIP_MAGIC_SECOND = 0x8b;

    /// <summary>
    /// The bundle's four-part version stamp as JSON, or null when the manifest carries none —
    /// a bundle written by a sidecar older than the stamp, which still reads. Also null when
    /// the manifest cannot be read at all; a caller that must tell "unstamped" from "unreadable"
    /// checks the bytes itself first.
    /// </summary>
    public static string? TryReadVersionStamp(byte[] bundle)
    {
        try
        {
            using JsonDocument manifest = JsonDocument.Parse(Inflate(bundle));

            if (!manifest.RootElement.TryGetProperty(VERSION_PROPERTY, out JsonElement version)
                || version.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string raw = version.GetRawText();

            return raw.Length > MAX_VERSION_STAMP_CHARS ? null : raw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gunzips a bundle, or returns it unchanged when it is not compressed — the runtime's own
    /// reader sniffs the same magic bytes, because bundles written before compression landed
    /// must still read. The whole manifest is inflated: the file map is skipped when the stamp
    /// is read, not when the bytes are decompressed.
    /// </summary>
    private static ReadOnlyMemory<byte> Inflate(byte[] bundle)
    {
        if (bundle.Length < 2 || bundle[0] != GZIP_MAGIC_FIRST || bundle[1] != GZIP_MAGIC_SECOND)
        {
            return bundle;
        }

        using var compressed = new MemoryStream(bundle, writable: false);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        // Clamped to the cap: the guess is a multiply on a length the sender chose, so left
        // unbounded it overflows int before the cap it is guessing at ever applies.
        using var inflated = new MemoryStream(
            (int)Math.Min(MAX_BUNDLE_BYTES, (long)bundle.Length * INFLATED_SIZE_GUESS));

        byte[] buffer = new byte[COPY_BUFFER_BYTES];
        long total = 0;
        int read;
        while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MAX_BUNDLE_BYTES)
            {
                throw new InvalidDataException(
                    $"Session bundle inflates past the {MAX_BUNDLE_BYTES}-byte cap");
            }

            inflated.Write(buffer, 0, read);
        }

        // The buffer, not a copy of it: the reader only walks the bytes, so the extra full-size
        // array ToArray would allocate buys nothing.
        return inflated.GetBuffer().AsMemory(0, (int)inflated.Length);
    }
}

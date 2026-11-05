using System.IO.Compression;
using System.Text;

namespace IniaAi.Domain.Serialization;

/// <summary>
/// Lossless compression for the full canonical workflow JSON stored on each <c>WorkflowVersion</c>.
/// Each version is a complete snapshot, so storing it compressed keeps the version table from
/// ballooning as prompt engineers save. The content hash is computed on the uncompressed text, so
/// compression never affects dedup or cross-instance comparison.
/// </summary>
public static class WorkflowJsonCompression
{
    public static byte[] Compress(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        return Compress(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>Compresses UTF-8 JSON already encoded, without a second copy as text.</summary>
    public static byte[] Compress(byte[] raw)
    {
        if (raw.Length == 0)
        {
            return [];
        }

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(raw, 0, raw.Length);
        }

        return output.ToArray();
    }

    public static string Decompress(byte[]? compressed)
    {
        if (compressed is null || compressed.Length == 0)
        {
            return string.Empty;
        }

        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);

        return Encoding.UTF8.GetString(output.ToArray());
    }
}

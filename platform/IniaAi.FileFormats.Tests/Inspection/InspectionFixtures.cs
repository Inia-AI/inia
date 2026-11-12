using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Tests.Unlock;
using IniaAi.Testing.Documents;
using SkiaSharp;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace IniaAi.FileFormats.Tests.Inspection;

internal static class InspectionFixtures
{
    internal static readonly string ResourcesRoot = Path.Combine(AppContext.BaseDirectory, "TestResources");

    internal static readonly string[] AcceptedExtensions =
        [".pdf", ".jpg", ".jpeg", ".png", ".csv", ".xls", ".xlsx", ".xlsm", ".docx", ".txt", ".md", ".markdown", ".msg", ".eml"];

    private const int FIXED_SEED = 20260914;
    private const int PLAIN_ZIP_ENTRIES = 3;

    internal sealed record SweepResult(int Inspected, IReadOnlyList<string> FalseRefusals, IReadOnlyList<string> Encrypted, IReadOnlyDictionary<string, int> Verdicts);

    internal static SweepResult Sweep(string root, int maxFilesPerExtension, long maxFileBytes)
    {
        var perExtension = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var verdicts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        List<string> falseRefusals = [];
        List<string> encrypted = [];
        int inspected = 0;

        foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
        {
            string path = file.FullName;
            string extension = file.Extension;
            int seen = perExtension.GetValueOrDefault(extension);
            if (file.Name.StartsWith('.') || seen >= maxFilesPerExtension || file.Length > maxFileBytes)
            {
                continue;
            }

            perExtension[extension] = seen + 1;
            inspected++;
            FileInspection verdict = FileContentInspector.Default.Inspect(File.ReadAllBytes(path), file.Name, FileInspectionLimits.Default);
            string key = $"{extension.ToLowerInvariant(),-10} {verdict.Outcome,-9} {verdict.DetectedMediaType ?? "-"} {verdict.ReasonCode ?? string.Empty}";
            verdicts[key] = verdicts.GetValueOrDefault(key) + 1;

            if (verdict.Outcome == FileInspectionOutcome.Refused && AcceptedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                falseRefusals.Add($"{Path.GetRelativePath(root, path)}\t{verdict.ReasonCode}\t{verdict.DetectedMediaType}\t{verdict.Detail}");
            }

            if (verdict.Outcome == FileInspectionOutcome.Encrypted)
            {
                encrypted.Add(Path.GetRelativePath(root, path));
            }
        }

        return new SweepResult(inspected, falseRefusals, encrypted, verdicts);
    }

    internal static byte[] Resource(params string[] pathParts)
    {
        return File.ReadAllBytes(Path.Combine([ResourcesRoot, .. pathParts]));
    }

    internal static byte[] GifHeader()
    {
        byte[] bytes = new byte[64];
        Encoding.ASCII.GetBytes("GIF89a").CopyTo(bytes, 0);
        return bytes;
    }

    internal static byte[] PlainZip()
    {
        return ZipOfEmptyEntries(PLAIN_ZIP_ENTRIES, "entry");
    }

    internal static byte[] ZipOfEmptyEntries(int entryCount, string content)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            for (int i = 0; i < entryCount; i++)
            {
                ZipArchiveEntry entry = archive.CreateEntry($"file-{i}.txt", CompressionLevel.NoCompression);
                using Stream stream = entry.Open();
                stream.Write(Encoding.ASCII.GetBytes(content));
            }
        }

        return ms.ToArray();
    }

    internal static byte[] PackageWithManifestOf(long manifestBytes)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (Stream workbook = archive.CreateEntry("xl/workbook.xml").Open())
            {
                workbook.Write(Encoding.ASCII.GetBytes("<workbook/>"));
            }

            using Stream manifest = archive.CreateEntry("[Content_Types].xml", CompressionLevel.Optimal).Open();
            byte[] spaces = new byte[64 * 1024];
            Array.Fill(spaces, (byte)' ');
            for (long written = 0; written < manifestBytes; written += spaces.Length)
            {
                manifest.Write(spaces);
            }
        }

        return ms.ToArray();
    }

    internal static byte[] TruncatedZip()
    {
        byte[] bytes = new byte[256];
        bytes[0] = 0x50;
        bytes[1] = 0x4B;
        bytes[2] = 0x03;
        bytes[3] = 0x04;
        new Random(FIXED_SEED).NextBytes(bytes.AsSpan(4));
        return bytes;
    }

    internal static byte[] BinaryJunk(int length)
    {
        byte[] bytes = new byte[length];
        new Random(FIXED_SEED).NextBytes(bytes);
        bytes[length / 2] = 0;
        return bytes;
    }

    internal static byte[] Windows1252Text()
    {
        return [.. Encoding.ASCII.GetBytes("name;city\r\nR"), 0xE9, .. Encoding.ASCII.GetBytes("my;Gen"), 0xE8, .. Encoding.ASCII.GetBytes("ve\r\n")];
    }

    internal static byte[] LegacyWordDocument()
    {
        return WordFixtures.Legacy();
    }

    /// <summary>A real 8 by 8 JPEG whose frame header is rewritten to claim <paramref name="width"/> by <paramref name="height"/> pixels.</summary>
    internal static byte[] JpegClaimingSize(ushort width, ushort height)
    {
        const int HEIGHT_OFFSET = 5;
        const int WIDTH_OFFSET = 7;
        using var bitmap = new SKBitmap(8, 8);
        using SKData encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 80);
        byte[] jpeg = encoded.ToArray();
        int frame = jpeg.AsSpan().IndexOf(new byte[] { 0xFF, 0xC0 });
        BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(frame + HEIGHT_OFFSET), height);
        BinaryPrimitives.WriteUInt16BigEndian(jpeg.AsSpan(frame + WIDTH_OFFSET), width);
        return jpeg;
    }

    internal static byte[] PngClaimingSize(uint width, uint height)
    {
        using var ms = new MemoryStream();
        ms.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        byte[] ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, width);
        WriteBigEndian(ihdr, 4, height);
        ihdr[8] = 8;
        WriteChunk(ms, "IHDR", ihdr);
        byte[] emptyZlibStream = [0x78, 0x9C, 0x03, 0x00, 0x00, 0x00, 0x00, 0x01];
        WriteChunk(ms, "IDAT", emptyZlibStream);
        WriteChunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);
        byte[] typeAndData = [.. Encoding.ASCII.GetBytes(type), .. data];
        stream.Write(typeAndData);
        byte[] crc = new byte[4];
        WriteBigEndian(crc, 0, Crc32(typeAndData));
        stream.Write(crc);
    }

    private static void WriteBigEndian(byte[] target, int offset, uint value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    // ImageSharp validates critical-chunk CRCs, so a fake header needs a real one; the solution references no hashing package.
    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }
}

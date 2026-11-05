using System.IO.Compression;

namespace IniaAi.Testing.Fixtures;

/// <summary>
/// Byte payloads that every intake mouth must decide the same way: bytes no detector names, an
/// archive that expands far past the limits, and the compound file that sent Aspose's format
/// detector walking declared sectors for 34 s and 22 GB.
/// </summary>
public static class HostileBytes
{
    private static readonly string _resourcesRoot = Path.Combine(AppContext.BaseDirectory, "TestResources");

    public static byte[] WindowsExecutable()
    {
        byte[] bytes = new byte[512];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        return bytes;
    }

    public static byte[] ZipBomb(long expandedBytes)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry("word/document.xml", CompressionLevel.Optimal);
            using Stream stream = entry.Open();
            byte[] zeros = new byte[1024 * 1024];
            for (long written = 0; written < expandedBytes; written += zeros.Length)
            {
                stream.Write(zeros);
            }
        }

        return ms.ToArray();
    }

    public static byte[] OutlookMessage()
    {
        return File.ReadAllBytes(Path.Combine(_resourcesRoot, "Email", "Monthly Newsletter.msg"));
    }

    // Six header bytes (directory-sector count, mini-FAT count, four DIFAT slots) that send a
    // detector walking declared sectors into tens of gigabytes; found by random mutation in review.
    public static byte[] MutatedCompoundFile(byte[] compoundFile)
    {
        byte[] mutated = (byte[])compoundFile.Clone();
        mutated[0x2A] = 61;
        mutated[0x42] = 190;
        mutated[0x7B] = 184;
        mutated[0x92] = 189;
        mutated[0x162] = 188;
        mutated[0x1A8] = 49;
        return mutated;
    }

    public static byte[] MutatedOutlookMessage()
    {
        return MutatedCompoundFile(OutlookMessage());
    }
}

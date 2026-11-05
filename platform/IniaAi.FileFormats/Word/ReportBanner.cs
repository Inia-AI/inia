using IniaAi.Common.Resources;

namespace IniaAi.FileFormats.Word;

/// <summary>The banner at the top of an exported report, embedded in this assembly for every report writer.</summary>
public static class ReportBanner
{
    private const string RESOURCE_FILE = "inia-banner.jpg";

    private static readonly Lazy<byte[]> _image = new(Read);

    public static byte[] Image => _image.Value;

    private static byte[] Read()
    {
        using Stream stream = typeof(ReportBanner).Assembly.ReadResource(RESOURCE_FILE);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}

using System.Globalization;

namespace IniaAi.FileFormats.Inspection;

/// <summary>A size as a message says it to a reader: in bytes, KB or MB.</summary>
public static class FileSizeText
{
    private const long BYTES_PER_KILOBYTE = 1024;
    private const long BYTES_PER_MEGABYTE = BYTES_PER_KILOBYTE * 1024;
    private const string ONE_DECIMAL_AT_MOST = "0.#";

    public static string Of(long bytes)
    {
        return bytes switch
        {
            >= BYTES_PER_MEGABYTE => (bytes / (double)BYTES_PER_MEGABYTE).ToString(ONE_DECIMAL_AT_MOST, CultureInfo.InvariantCulture) + " MB",
            >= BYTES_PER_KILOBYTE => (bytes / (double)BYTES_PER_KILOBYTE).ToString(ONE_DECIMAL_AT_MOST, CultureInfo.InvariantCulture) + " KB",
            _ => bytes.ToString(CultureInfo.InvariantCulture) + " bytes",
        };
    }
}

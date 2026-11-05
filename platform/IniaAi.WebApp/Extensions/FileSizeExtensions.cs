namespace IniaAi.WebApp.Extensions;

public static class FileSizeExtensions
{
    public static string ToFileSize(this long sizeBytes)
    {
        if (sizeBytes >= 1024 * 1024)
        {
            return $"{sizeBytes / (1024.0 * 1024.0):0.#} MiB";
        }

        return sizeBytes >= 1024 ? $"{sizeBytes / 1024.0:0.#} KiB" : "<1 KiB";
    }
}

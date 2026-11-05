using Microsoft.Extensions.Logging;

namespace IniaAi.Services.FileDownload;

internal static class DownloadManager
{
    public static async Task<bool> EnsureDownloadFinishedAsync(string directory, ILogger logger)
    {
        logger.LogDebug("Ensure download finished in directory: {Directory}", directory);

        if (!Directory.Exists(directory))
        {
            logger.LogWarning("Directory does not exist: {Directory}", directory);

            return false;
        }

        var fileInfo = new Dictionary<string, long>();
        bool isFinished = false;

        while (!isFinished)
        {
            isFinished = CheckDownloadProgress(directory, fileInfo, logger);
            await Task.Delay(250);
        }

        logger.LogDebug("Download finished in directory: {Directory}", directory);

        return true;
    }

    private static bool CheckDownloadProgress(
        string directory,
        Dictionary<string, long> fileInfo,
        ILogger logger)
    {
        bool isFinished = true;

        foreach (string file in Directory.GetFiles(directory))
        {
            if (!fileInfo.TryGetValue(file, out long lastSize))
            {
                isFinished = false;
                fileInfo[file] = 0;
                logger.LogTrace("File added: {File}", file);
                continue;
            }

            long currentSize = new FileInfo(file).Length;
            logger.LogTrace("File size: {File} {Size}, last size: {LastSize}",
                file, currentSize, lastSize);

            if (lastSize != currentSize)
            {
                isFinished = false;
            }

            fileInfo[file] = currentSize;
        }

        return isFinished;
    }
}

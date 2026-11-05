using IniaAi.Services.FileDownload;
using IniaAi.Testing;
using Microsoft.Extensions.Logging;

namespace IniaAi.Services.Tests.FileDownload;

public class DownloadManagerTests
{
    private readonly ITestOutputHelper _outputHelper;

    public DownloadManagerTests(ITestOutputHelper outputHelper)
    {
        _outputHelper = outputHelper;
    }

    [Fact]
    public async Task EnsureDownloadFinishedAsync_WhenCalledNonEmptyDirectory_ShouldReturnTrueAsync()
    {
        // Arrange
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _ = Directory.CreateDirectory(directory);
        string fileName = Path.Combine(directory, "file.txt");
        File.WriteAllText(fileName, "test");
        ILogger logger = TestsLogger.CreateLogger(_outputHelper);

        // Act
        bool result = await DownloadManager.EnsureDownloadFinishedAsync(directory, logger);

        // Assert
        Assert.True(Directory.Exists(directory));
        Assert.True(result);

        // Clean up
        Directory.Delete(directory, true);
    }

    [Fact]
    public async Task EnsureDownloadFinishedAsync_WhenCalledEmptyDirectory_ShouldReturnTrueAsync()
    {
        // Arrange
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _ = Directory.CreateDirectory(directory);
        ILogger logger = TestsLogger.CreateLogger(_outputHelper);

        // Act
        bool result = await DownloadManager.EnsureDownloadFinishedAsync(directory, logger);

        // Assert
        Assert.True(Directory.Exists(directory));
        Assert.True(result);

        // Clean up
        Directory.Delete(directory, true);
    }

    [Fact]
    public async Task EnsureDownloadFinishedAsync_WhenCalledDirectoryDoesNotExist_ShouldReturnTrueAsync()
    {
        // Arrange
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        ILogger logger = TestsLogger.CreateLogger(_outputHelper);

        // Act
        bool result = await DownloadManager.EnsureDownloadFinishedAsync(directory, logger);

        // Assert
        Assert.False(Directory.Exists(directory));
        Assert.False(result);
    }
}

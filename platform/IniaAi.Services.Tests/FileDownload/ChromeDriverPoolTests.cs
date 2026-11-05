using IniaAi.Common.Http;
using IniaAi.Services.FileDownload;
using IniaAi.Testing.XunitAttributes;
using Microsoft.Extensions.Logging.Abstractions;
using OpenQA.Selenium.Chrome;

namespace IniaAi.Services.Tests.FileDownload;

public class ChromeDriverPoolTests
{
    [RequiresChromeFact]
    public async Task AcquireDriverAsync_ShouldReturnDriver_WhenCalledAsync()
    {
        // Arrange
        var pool = new ChromeDriverPool(1);
        string storageDir = Guid.NewGuid().ToString();

        // Act
        ChromeDriver driver = await pool.AcquirePageDriverAsync(storageDir, OutboundAddressGuard.For([]), NullLogger.Instance);

        // Assert
        Assert.NotNull(driver);
        pool.ReleaseDriver(driver);
    }

    [RequiresChromeFact]
    public async Task AcquireDriverAsync_ShouldCreateNewDriver_WhenDifferentStorageDirAsync()
    {
        // Arrange
        var pool = new ChromeDriverPool(1);
        string storageDir1 = Guid.NewGuid().ToString();
        string storageDir2 = Guid.NewGuid().ToString();

        // Act
        ChromeDriver driver1 = await pool.AcquirePageDriverAsync(storageDir1, OutboundAddressGuard.For([]), NullLogger.Instance);
        pool.ReleaseDriver(driver1);
        ChromeDriver driver2 = await pool.AcquirePageDriverAsync(storageDir2, OutboundAddressGuard.For([]), NullLogger.Instance);

        // Assert
        Assert.NotEqual(driver1, driver2);
        pool.ReleaseDriver(driver2);
    }

    [Fact]
    public void CreateService_UsesTheNamedDriver_SoSeleniumManagerNeverRuns()
    {
        // Arrange
        string directory = Path.Combine(Path.GetTempPath(), "drivers");

        // Act
        using ChromeDriverService service = ChromeDriverPool.CreateService(Path.Combine(directory, "chromedriver"));

        // Assert
        Assert.Equal(directory, service.DriverServicePath);
        Assert.Equal("chromedriver", service.DriverServiceExecutableName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void CreateService_LeavesTheDriverToSelenium_WhenNoneIsNamed(string? driverPath)
    {
        // Act
        using ChromeDriverService service = ChromeDriverPool.CreateService(driverPath);

        // Assert
        Assert.Null(service.DriverServicePath);
    }

    [Fact]
    public async Task AcquirePageDriverAsync_ShouldRefuseAndCountThePage_WhenTheSandboxDoesNotStartAsync()
    {
        // Arrange
        using var pool = new ChromeDriverPool(1, new BrowserIsolation(false, null, "Chrome runs without its sandbox."));

        // Act
        BrowserSandboxUnavailableException refusal = await Assert.ThrowsAsync<BrowserSandboxUnavailableException>(
            () => pool.AcquirePageDriverAsync(Guid.NewGuid().ToString(), OutboundAddressGuard.For([]), NullLogger.Instance, TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal("Chrome runs without its sandbox.", refusal.Message);
        Assert.Equal(1, pool.RefusedPages);
    }

    [RequiresChromeFact]
    public async Task ReleaseDriver_ShouldDisposeDriver_WhenPoolIsDisposedAsync()
    {
        // Arrange
        var pool = new ChromeDriverPool(1);
        string storageDir = Guid.NewGuid().ToString();
        ChromeDriver driver = await pool.AcquirePageDriverAsync(storageDir, OutboundAddressGuard.For([]), NullLogger.Instance);

        // Act
        pool.ReleaseDriver(driver);

        // Assert
        _ = Assert.Throws<ObjectDisposedException>(() => driver.Url = "about:blank");

        pool.Dispose();
    }

    [RequiresChromeFact]
    public async Task ReleaseDriver_ShouldFreeTheSlotOnce_WhenCalledTwiceAsync()
    {
        // Arrange
        using var pool = new ChromeDriverPool(1);
        ChromeDriver driver = await pool.AcquirePageDriverAsync(Guid.NewGuid().ToString(), OutboundAddressGuard.For([]), NullLogger.Instance);

        // Act
        pool.ReleaseDriver(driver);
        pool.ReleaseDriver(driver);

        // Assert
        ChromeDriver next = await pool.AcquirePageDriverAsync(Guid.NewGuid().ToString(), OutboundAddressGuard.For([]), NullLogger.Instance);
        pool.ReleaseDriver(next);
    }

    [RequiresChromeFact]
    public async Task Dispose_ShouldDisposeAllDriversAsync()
    {
        // Arrange
        var pool = new ChromeDriverPool(2);
        string storageDir1 = Guid.NewGuid().ToString();
        string storageDir2 = Guid.NewGuid().ToString();
        ChromeDriver driver1 = await pool.AcquirePageDriverAsync(storageDir1, OutboundAddressGuard.For([]), NullLogger.Instance);
        ChromeDriver driver2 = await pool.AcquirePageDriverAsync(storageDir2, OutboundAddressGuard.For([]), NullLogger.Instance);

        // Act
        pool.Dispose();

        // Assert
        _ = Assert.Throws<ObjectDisposedException>(() => driver1.Url = "about:blank");
        _ = Assert.Throws<ObjectDisposedException>(() => driver2.Url = "about:blank");
    }
}

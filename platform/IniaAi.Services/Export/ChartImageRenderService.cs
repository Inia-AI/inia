using IniaAi.Common.Interfaces;
using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Services.FileDownload;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IniaAi.Services.Export;

/// <summary>
/// Draws a Dynamic Chart block's chart as a PNG for the Word export, in headless Chrome, with the chart bundle the Vue
/// app builds (<c>Resources/chartBuilder</c>), so the picture matches the chart on screen.
/// </summary>
/// <remarks>
/// Uses the existing ChromeDriverPool infrastructure to render charts in-memory.
/// No web server or authentication is required - HTML is loaded via data URI.
/// </remarks>
public class ChartImageRenderService : IChartImageRenderService, IChartImageSessions
{
    private const string CHART_BUNDLE_RESOURCE = "IniaAi.Services.Resources.chartBuilder.charts.global.js";
    private const string DRAWN = "drawn";

    private static readonly Lazy<string> _chartBundle = new(() =>
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(CHART_BUNDLE_RESOURCE)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{CHART_BUNDLE_RESOURCE}' not found. Copy chart-builder/ of the frontend repo " +
                "(npm run build:chart-builder) to Resources/chartBuilder/.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    });

    /// <summary>
    /// The page every chart loads: the bundle and an empty container. It is built once, because the bundle is large
    /// and each chart gets its own Chrome; the chart's configuration goes to the page as a script argument.
    /// </summary>
    private static readonly Lazy<string> _chartPageUri = new(() =>
        $"data:text/html;charset=utf-8;base64,{Convert.ToBase64String(Encoding.UTF8.GetBytes(ChartPageHtml(string.Empty)))}");

    private static readonly JsonSerializerOptions _configJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly string _exportStyleJson = JsonSerializer.Serialize(new
    {
        fontFamily = "Calibri, 'Segoe UI', Helvetica, Arial, sans-serif",
        titleFontSize = 15,
        axisTitleFontSize = 12,
        labelFontSize = 11,
    });

    private const string DRAW_SCRIPT = $$"""
        const [config, style, width, height, done] = arguments;
        const container = document.getElementById('chart-container');
        container.style.width = width + 'px';
        container.style.height = height + 'px';
        const failed = error => done(String((error && error.message) || error));
        try {
            IniaCharts.drawForExport(container, JSON.parse(config), JSON.parse(style)).then(() => done('{{DRAWN}}'), failed);
        } catch (error) {
            failed(error);
        }
        """;

    private readonly ChartRenderingSettings _settings;
    private readonly ILogger<ChartImageRenderService> _logger;

    public ChartImageRenderService(
        IOptions<PlatformSettings> platformSettings,
        ILogger<ChartImageRenderService> logger)
    {
        _settings = platformSettings.Value.ChartRendering;
        _logger = logger;
    }

    /// <summary>
    /// Draws one chart to a PNG in a Chrome of its own, or throws <see cref="InvalidOperationException"/> when it cannot.
    /// An export opens a session instead, so that one Chrome draws all its charts.
    /// </summary>
    public async Task<byte[]> RenderChartToImageAsync(
        object chartConfig,
        int width,
        int height,
        string? reportContext = null,
        CancellationToken cancellationToken = default)
    {
        await using IChartImageSession session = Open();
        return await session.RenderChartToImageAsync(chartConfig, width, height, reportContext, cancellationToken);
    }

    public IChartImageSession Open()
    {
        return new Session(this);
    }

    private string BaseTempPath => string.IsNullOrWhiteSpace(_settings.TempDirectory)
        ? Path.GetTempPath()
        : Path.GetFullPath(_settings.TempDirectory);

    /// <summary>Draws one chart with <paramref name="driver"/>, or throws <see cref="InvalidOperationException"/>.</summary>
    private byte[] DrawChart(ChromeDriver driver, object chartConfig, int width, int height, string? reportContext)
    {
        string? chartTitle = (chartConfig as ChartConfiguration)?.Title;
        _logger.LogInformation("Chart rendering - Title: {Title}, Context: {Context}", chartTitle ?? "NULL", reportContext ?? "NULL");

        try
        {
            // Enum-as-string keeps options.stacking on the TS ChartConfig shape the bundle expects.
            string chartConfigJson = JsonSerializer.Serialize(chartConfig, _configJson);
            _logger.LogDebug("Chart config JSON: {ConfigJson}", chartConfigJson);

            if (_settings.DebugPreserveTempFiles)
            {
                SaveDebugPage(chartConfigJson, width, height, chartTitle, reportContext);
            }

            driver.Navigate().GoToUrl(_chartPageUri.Value);

            string result = Draw(driver, chartConfigJson, width, height);
            if (result != DRAWN)
            {
                throw new ChartNotDrawnException(result);
            }

            LogBrowserConsole(driver);

            IWebElement chartElement = driver.FindElement(By.Id("chart-container"));
            byte[] screenshot = ((ITakesScreenshot)chartElement).GetScreenshot().AsByteArray;

            _logger.LogInformation("Chart rendered successfully: {Size} bytes", screenshot.Length);

            return screenshot;
        }
        catch (ChartNotDrawnException ex)
        {
            _logger.LogWarning("The chart page drew no chart: {Reason}", ex.Reason);
            throw;
        }
        catch (WebDriverTimeoutException ex)
        {
            _logger.LogError(ex, "Timeout waiting for chart to render");
            throw new InvalidOperationException("Chart rendering timed out. The chart may contain invalid configuration.", ex);
        }
        catch (NoSuchElementException ex)
        {
            _logger.LogError(ex, "Chart container element not found");
            throw new InvalidOperationException("Chart container not found. The chart may have failed to initialize.", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to render chart to image");
            throw new InvalidOperationException("Failed to render chart to image. See inner exception for details.", ex);
        }
    }

    /// <summary>Saves a page that draws the chart when it opens in a browser, under the report's and the chart's names.</summary>
    private void SaveDebugPage(string chartConfigJson, int width, int height, string? chartTitle, string? reportContext)
    {
        string folder = Path.Combine(
            BaseTempPath,
            SanitizeForFilename(reportContext),
            $"{SanitizeForFilename(chartTitle)}_{Guid.NewGuid().ToString("N")[..8]}");
        _ = Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, "chart_debug.html");
        File.WriteAllText(path, ChartPageHtml(DebugDrawScript(chartConfigJson, width, height)));
        _logger.LogInformation("Debug mode: saved the chart page to {Path}", path);
    }

    /// <summary>What the page reports once the chart settles: <see cref="DRAWN"/>, or why it drew nothing.</summary>
    private string Draw(ChromeDriver driver, string chartConfigJson, int width, int height)
    {
        driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromMilliseconds(_settings.RenderTimeoutMs);
        try
        {
            return ((IJavaScriptExecutor)driver).ExecuteAsyncScript(DRAW_SCRIPT, chartConfigJson, _exportStyleJson, width, height) as string ?? string.Empty;
        }
        catch (WebDriverTimeoutException)
        {
            LogBrowserConsole(driver);
            throw;
        }
    }

    private void LogBrowserConsole(ChromeDriver driver)
    {
        try
        {
            ReadOnlyCollection<LogEntry> logs = driver.Manage().Logs.GetLog("browser");
            foreach (LogEntry log in logs)
            {
                if (log.Level == OpenQA.Selenium.LogLevel.Severe)
                {
                    _logger.LogError("Browser console [{Level}]: {Message}", log.Level, log.Message);
                }
                else
                {
                    _logger.LogDebug("Browser console [{Level}]: {Message}", log.Level, log.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not retrieve browser console logs");
        }
    }

    /// <summary>The chart bundle and an empty chart container, then <paramref name="script"/>. The page makes no network request.</summary>
    private static string ChartPageHtml(string script)
    {
        return $@"<!DOCTYPE html>
<html>
<head>
    <meta charset=""utf-8"">
    <title>Chart Rendering</title>
    <script>{_chartBundle.Value}</script>
    <style>
        body {{
            margin: 0;
            padding: 0;
            background: white;
            overflow: hidden;
        }}
    </style>
</head>
<body>
    <div id=""chart-container""></div>
    {script}
</body>
</html>";
    }

    /// <summary>A script that draws the chart when a saved debug page opens in a browser.</summary>
    private static string DebugDrawScript(string chartConfigJson, int width, int height)
    {
        return $@"<script>
        const container = document.getElementById('chart-container');
        container.style.width = '{width}px';
        container.style.height = '{height}px';
        IniaCharts.drawForExport(container, {chartConfigJson}, {_exportStyleJson});
    </script>";
    }

    /// <summary>
    /// One Chrome for the charts of one export, started for the first chart and quit when the session is disposed. A
    /// chart that fails for a reason other than the page's own refusal quits the Chrome, and the next chart starts a new
    /// one. Draws one chart at a time.
    /// </summary>
    private sealed class Session(ChartImageRenderService service) : IChartImageSession
    {
        private ChromeDriver? _driver;
        private string? _downloadDir;

        public async Task<byte[]> RenderChartToImageAsync(
            object chartConfig,
            int width,
            int height,
            string? reportContext = null,
            CancellationToken cancellationToken = default)
        {
            ChromeDriver driver = _driver ?? await StartChromeAsync(cancellationToken);
            try
            {
                return service.DrawChart(driver, chartConfig, width, height, reportContext);
            }
            catch (ChartNotDrawnException)
            {
                throw;
            }
            catch
            {
                QuitChrome();
                throw;
            }
        }

        public ValueTask DisposeAsync()
        {
            QuitChrome();
            return ValueTask.CompletedTask;
        }

        private async Task<ChromeDriver> StartChromeAsync(CancellationToken cancellationToken)
        {
            _downloadDir = Path.Combine(service.BaseTempPath, $"chart-session-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(_downloadDir);
            try
            {
                _driver = await ChromeDriverPoolProvider.Instance.AcquireOfflineDriverAsync(_downloadDir, service._logger, cancellationToken);
                return _driver;
            }
            catch (Exception ex)
            {
                QuitChrome();
                service._logger.LogError(ex, "Failed to render chart to image");
                throw new InvalidOperationException("Failed to render chart to image. See inner exception for details.", ex);
            }
        }

        private void QuitChrome()
        {
            if (_driver is not null)
            {
                ChromeDriverPoolProvider.Instance.ReleaseDriver(_driver);
                _driver = null;
            }

            if (_downloadDir is null)
            {
                return;
            }

            try
            {
                Directory.Delete(_downloadDir, recursive: true);
            }
            catch (Exception ex)
            {
                service._logger.LogWarning(ex, "Failed to cleanup temp directory: {TempDir}", _downloadDir);
            }

            _downloadDir = null;
        }
    }

    /// <summary>The page refused the chart, such as one with no data, or its script failed. The Word writer puts in a table.</summary>
    private sealed class ChartNotDrawnException(string reason) : InvalidOperationException($"The chart page could not draw the chart: {reason}")
    {
        public string Reason { get; } = reason;
    }

    /// <summary>
    /// Sanitizes a string for use as a filename by removing invalid characters.
    /// </summary>
    private static string SanitizeForFilename(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        // Remove invalid filename characters
        char[] invalidChars = Path.GetInvalidFileNameChars();
        string sanitized = string.Join("_", input.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Limit length to avoid path too long issues
        const int maxLength = 50;
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized[..maxLength];
        }

        return sanitized.Trim('_', ' ');
    }
}

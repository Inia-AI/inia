namespace IniaAi.Configuration.Platform;

/// <summary>
/// Settings for drawing each chart of a Word export as a picture in headless Chrome, with the chart bundle the Vue app
/// builds (IniaAi.Services/Resources/chartBuilder/README.md).
/// </summary>
public class ChartRenderingSettings
{
    /// <summary>
    /// Gets or sets the timeout in milliseconds to wait for the chart page to finish drawing.
    /// </summary>
    public int RenderTimeoutMs { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the directory for temporary chart rendering files.
    /// If null or empty, uses system temp directory (Path.GetTempPath()).
    /// For development, can use "./temp/charts" for solution-relative path.
    /// </summary>
    /// <remarks>
    /// Development: Use solution-relative path like "./temp/charts" for easier debugging.
    /// Production: Leave null/empty to use system temp directory for security and cleanup.
    /// The directory will be created if it doesn't exist.
    /// </remarks>
    public string? TempDirectory { get; set; }

    /// <summary>
    /// Gets or sets whether to preserve temporary chart files for debugging.
    /// When true, temp directories with chart HTML are not deleted after rendering.
    /// </summary>
    /// <remarks>
    /// Development: Set to true to inspect generated HTML and troubleshoot rendering issues.
    /// Production: Must be false to prevent disk space issues.
    /// Temp directory names will include chart title for easy identification.
    /// </remarks>
    public bool DebugPreserveTempFiles { get; set; } = false;
}

namespace IniaAi.Common.Interfaces;

/// <summary>
/// Draws a Dynamic Chart block's chart as a PNG for the Word export, the way the Vue app draws it on screen.
/// </summary>
public interface IChartImageRenderService
{
    /// <summary>
    /// Renders a chart configuration to a PNG image.
    /// </summary>
    /// <param name="chartConfig">The block's chart configuration (will be JSON serialized).</param>
    /// <param name="width">Width of the rendered image in pixels.</param>
    /// <param name="height">Height of the rendered image in pixels.</param>
    /// <param name="reportContext">Optional report/workflow name to group charts from the same report together in temp directories.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>PNG image as byte array.</returns>
    /// <exception cref="InvalidOperationException">Thrown when chart rendering fails.</exception>
    Task<byte[]> RenderChartToImageAsync(
        object chartConfig,
        int width,
        int height,
        string? reportContext = null,
        CancellationToken cancellationToken = default);
}

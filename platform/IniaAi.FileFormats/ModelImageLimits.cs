using IniaAi.Common.Content;
using IniaAi.FileFormats.Pdf;

namespace IniaAi.FileFormats;

/// <summary>
/// Raster limits shared by every path that renders or forwards document content to a model
/// (PDF pages, standalone images, Word image regions).
/// </summary>
public static class ModelImageLimits
{
    /// <summary>
    /// Anthropic rejects request images larger than 8000px on either side.
    /// </summary>
    public const int MAX_PROVIDER_IMAGE_EDGE_PX = 8000;

    /// <summary>
    /// Anthropic holds every image of a request that carries more than 20 images to this edge. Paths
    /// that send many page images in one request render for <see cref="VisionTier.Standard"/>, which
    /// stays under it.
    /// </summary>
    public const int MANY_IMAGE_REQUEST_EDGE_PX = 2000;

    /// <summary>
    /// The highest DPI any render uses, so a page smaller than the reading model's tier is not
    /// rendered finer than its content can carry.
    /// </summary>
    public const int MAX_RENDER_DPI = 300;

    /// <summary>
    /// The DPI, up to <paramref name="dpiCap"/>, that puts a source's longest edge on the long edge
    /// of <paramref name="visionTier"/>. The render is not shrunk further to the tier's visual-token
    /// limit: the model's own downscale to that limit keeps fine, faint text that a smaller Aspose
    /// render loses.
    /// </summary>
    public static int ResolveRenderDpi(double widthPt, double heightPt, VisionTier visionTier, int dpiCap = MAX_RENDER_DPI)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(visionTier.LongEdgePx);

        double longestEdgePt = Math.Max(widthPt, heightPt);
        if (longestEdgePt <= 0)
        {
            return dpiCap;
        }

        return Math.Clamp((int)Math.Floor(visionTier.LongEdgePx * PdfUtils.POINTS_PER_INCH / longestEdgePt), 1, dpiCap);
    }
}

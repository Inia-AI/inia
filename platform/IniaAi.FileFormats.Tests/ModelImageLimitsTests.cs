using IniaAi.Common.Content;

namespace IniaAi.FileFormats.Tests;

public class ModelImageLimitsTests
{
    [Theory]
    [InlineData(595.0, 842.0, false, 134)]
    [InlineData(595.0, 842.0, true, 220)]
    [InlineData(842.0, 595.0, true, 220)]
    [InlineData(720.0, 540.0, false, 156)]
    [InlineData(1920.0, 842.0, false, 58)]
    [InlineData(2592.0, 842.0, true, 71)]
    [InlineData(200000.0, 100.0, false, 1)]
    public void ResolveRenderDpi_PutsTheLongestEdgeOnTheTiersLongEdge(double widthPt, double heightPt, bool highResolution, int expectedDpi)
    {
        VisionTier tier = highResolution ? VisionTier.HighResolution : VisionTier.Standard;

        Assert.Equal(expectedDpi, ModelImageLimits.ResolveRenderDpi(widthPt, heightPt, tier));
    }

    [Theory]
    [InlineData(100.0)]
    [InlineData(0.0)]
    public void ResolveRenderDpi_NeverRendersAboveTheDpiCap(double edgePt)
    {
        Assert.Equal(
            ModelImageLimits.MAX_RENDER_DPI,
            ModelImageLimits.ResolveRenderDpi(edgePt, edgePt, VisionTier.HighResolution));
    }

    [Fact]
    public void ResolveRenderDpi_UnderALowerCap_StopsAtTheCap()
    {
        Assert.Equal(100, ModelImageLimits.ResolveRenderDpi(595.0, 842.0, VisionTier.Standard, dpiCap: 100));
    }

    [Fact]
    public void ResolveRenderDpi_WithoutTheReadingModelsTier_Throws()
    {
        _ = Assert.Throws<ArgumentOutOfRangeException>(() => ModelImageLimits.ResolveRenderDpi(595.0, 842.0, default));
    }

    [Fact]
    public void ManyImageRequests_StayUnderTheStricterPerImageLimit()
    {
        Assert.True(VisionTier.STANDARD_LONG_EDGE_PX <= ModelImageLimits.MANY_IMAGE_REQUEST_EDGE_PX);
    }
}

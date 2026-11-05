using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.FileFormats.Images;
using IniaAi.FileFormats.Tests.Images;

namespace IniaAi.FileFormats.Tests.Pdf;

public class ImageCompressorTests
{
    private const int TARGET_BYTES = 100_000;

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public void CompressJpeg_APictureOverTheTarget_EndsAtOrUnderIt(int timesTheTarget)
    {
        BinaryContent picture = Jpeg(TARGET_BYTES * timesTheTarget);
        Assert.True(picture.BinaryData.Length > TARGET_BYTES, $"The generated picture of {picture.BinaryData.Length} bytes is not over the target.");

        BinaryContent compressed = ImageCompressor.CompressJpeg(picture, TARGET_BYTES);

        Assert.True(compressed.BinaryData.Length <= TARGET_BYTES, $"Compressed picture of {compressed.BinaryData.Length} bytes is over the {TARGET_BYTES}-byte target.");
        Assert.Equal(MediaType.ImageJpeg, compressed.MediaType);
    }

    [Fact]
    public void CompressJpeg_APictureUnderTheTarget_IsLeftAlone()
    {
        BinaryContent picture = Jpeg(TARGET_BYTES / 2);

        Assert.Same(picture, ImageCompressor.CompressJpeg(picture, TARGET_BYTES));
    }

    private static BinaryContent Jpeg(int approximateBytes)
    {
        using MemoryStream imageStream = ImageGenerator.GenerateJpegImage(approximateBytes);

        return new BinaryContent(imageStream.ToArray(), MediaType.ImageJpeg);
    }
}

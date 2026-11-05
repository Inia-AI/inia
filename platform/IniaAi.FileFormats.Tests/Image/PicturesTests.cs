using System.Diagnostics;
using IniaAi.FileFormats.Images;
using IniaAi.FileFormats.Tests.Inspection;
using SkiaSharp;

namespace IniaAi.FileFormats.Tests.Images;

public class PicturesTests
{
    private const int MAX_EDGE = 1568;

    [Theory]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    public void ReadSize_ReadsTheSizeOfAPngOrJpeg(SKEncodedImageFormat format)
    {
        byte[] picture = Encoded(320, 200, format);

        Assert.Equal(new PictureSize(320, 200), Pictures.ReadSize(picture));
    }

    [Fact]
    public void ReadSize_ReadsTheDeclaredSizeFromTheHeaderAlone()
    {
        byte[] huge = InspectionFixtures.PngClaimingSize(40_000, 40_000);

        Assert.Equal(new PictureSize(40_000, 40_000), Pictures.ReadSize(huge));
    }

    [Fact]
    public void ReadSize_OfBytesThatAreNoPicture_IsNull()
    {
        Assert.Null(Pictures.ReadSize("this is not a picture"u8.ToArray()));
    }

    /// <summary>
    /// GHSA-wmxv-xphr-5c9g: this 24-byte BigTIFF kept ImageSharp 3.1.12 looping for billions of
    /// steps. The platform's picture library does not read TIFF, so it answers at once.
    /// </summary>
    [Fact]
    public void ReadSize_OfTheBigTiffThatHungImageSharp_IsNullAtOnce()
    {
        byte[] bigTiff = new byte[24];
        "II"u8.CopyTo(bigTiff);
        BitConverter.GetBytes((ushort)43).CopyTo(bigTiff, 2);
        BitConverter.GetBytes((ushort)8).CopyTo(bigTiff, 4);
        BitConverter.GetBytes(16UL).CopyTo(bigTiff, 8);
        BitConverter.GetBytes(5_000_000_000UL).CopyTo(bigTiff, 16);

        var stopwatch = Stopwatch.StartNew();
        PictureSize? size = Pictures.ReadSize(bigTiff);

        Assert.Null(size);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1), $"reading took {stopwatch.Elapsed}");
    }

    [Fact]
    public void ToPngWithin_ScalesAPictureOverTheEdgeDownToIt_KeepingItsShape()
    {
        byte[] wide = Encoded(3200, 800, SKEncodedImageFormat.Jpeg);

        byte[] png = Pictures.ToPngWithin(wide, MAX_EDGE);

        Assert.Equal(SKEncodedImageFormat.Png, FormatOf(png));
        Assert.Equal(new PictureSize(MAX_EDGE, 392), Pictures.ReadSize(png));
    }

    [Fact]
    public void ToPngWithin_KeepsTheSizeOfAPictureWithinTheEdge()
    {
        byte[] small = Encoded(200, 150, SKEncodedImageFormat.Jpeg);

        byte[] png = Pictures.ToPngWithin(small, MAX_EDGE);

        Assert.Equal(SKEncodedImageFormat.Png, FormatOf(png));
        Assert.Equal(new PictureSize(200, 150), Pictures.ReadSize(png));
    }

    [Fact]
    public void ToPngWithin_OfBytesThatAreNoPicture_Throws()
    {
        _ = Assert.Throws<InvalidDataException>(() => Pictures.ToPngWithin("this is not a picture"u8.ToArray(), MAX_EDGE));
    }

    private static byte[] Encoded(int width, int height, SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(SKColors.SteelBlue);
        using SKData data = bitmap.Encode(format, 90);

        return data.ToArray();
    }

    private static SKEncodedImageFormat FormatOf(byte[] bytes)
    {
        using SKCodec codec = SKCodec.Create(new MemoryStream(bytes));

        return codec.EncodedFormat;
    }
}

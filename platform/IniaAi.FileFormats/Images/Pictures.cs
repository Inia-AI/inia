using SkiaSharp;

namespace IniaAi.FileFormats.Images;

/// <summary>
/// The picture work the platform does, on SkiaSharp: a picture's size read from its header, and a
/// picture scaled and saved again. SkiaSharp reads PNG, JPEG, GIF, WebP and BMP, not TIFF. Bytes it
/// cannot read throw <see cref="InvalidDataException"/>.
/// </summary>
public static class Pictures
{
    public const string NOT_A_PICTURE = "The bytes are not a picture the platform reads.";

    private static readonly SKSamplingOptions _bicubic = new(SKCubicResampler.CatmullRom);

    /// <summary>The size the header declares, or null when the bytes are no picture. Decodes no pixels.</summary>
    public static PictureSize? ReadSize(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using SKCodec codec = SKCodec.Create(stream, out SKCodecResult result);

        return result is SKCodecResult.Success ? new PictureSize(codec.Info.Width, codec.Info.Height) : null;
    }

    /// <summary>The picture as a PNG whose longest edge is at most <paramref name="maxLongestEdge"/>.</summary>
    public static byte[] ToPngWithin(byte[] bytes, int maxLongestEdge)
    {
        using SKBitmap picture = Decode(bytes);

        int longestEdge = Math.Max(picture.Width, picture.Height);
        if (longestEdge <= maxLongestEdge)
        {
            return Encode(picture, SKEncodedImageFormat.Png, 100);
        }

        double scale = (double)maxLongestEdge / longestEdge;
        using SKBitmap scaled = Resize(
            picture,
            Math.Max(1, (int)Math.Round(picture.Width * scale)),
            Math.Max(1, (int)Math.Round(picture.Height * scale)));

        return Encode(scaled, SKEncodedImageFormat.Png, 100);
    }

    internal static SKBitmap Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);

        return Decode(stream);
    }

    internal static SKBitmap Decode(Stream stream)
    {
        using SKCodec codec = SKCodec.Create(stream, out SKCodecResult result);
        if (result is not SKCodecResult.Success)
        {
            throw new InvalidDataException(NOT_A_PICTURE);
        }

        return SKBitmap.Decode(codec) ?? throw new InvalidDataException(NOT_A_PICTURE);
    }

    internal static SKBitmap Resize(SKBitmap picture, int width, int height)
    {
        return picture.Resize(new SKImageInfo(width, height, picture.ColorType, picture.AlphaType), _bicubic)
            ?? throw new InvalidDataException($"The picture cannot be scaled to {width} by {height} pixels.");
    }

    internal static byte[] Encode(SKBitmap picture, SKEncodedImageFormat format, int quality)
    {
        using SKData data = picture.Encode(format, quality)
            ?? throw new InvalidDataException($"The picture cannot be saved as {format}.");

        return data.ToArray();
    }
}

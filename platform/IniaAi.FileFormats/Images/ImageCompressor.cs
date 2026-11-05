using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Exceptions;
using SkiaSharp;

namespace IniaAi.FileFormats.Images;

public class ImageCompressor
{
    private const int RESIZED_JPEG_QUALITY = 75;

    /// <summary>
    /// The JPEG as it is when it holds at most <paramref name="targetSize"/> bytes; otherwise the
    /// picture scaled down in 5% steps and saved at quality 75 until it fits.
    /// </summary>
    /// <exception cref="IniaAiInputException"/>
    public static BinaryContent CompressJpeg(BinaryContent jpeg, int targetSize)
    {
        if (jpeg.BinaryData.Length <= targetSize)
        {
            return jpeg;
        }

        using Stream jpegStream = jpeg.BinaryData.ToStream();
        using SKBitmap original = Pictures.Decode(jpegStream);

        return new BinaryContent(ShrinkUnder(original, targetSize), MediaType.ImageJpeg);
    }

    private static byte[] ShrinkUnder(SKBitmap original, int targetSize)
    {
        double scaleFactor = 0.9;

        while (scaleFactor >= 0.05)
        {
            scaleFactor -= 0.05;
            int newWidth = Math.Max(1, (int)(original.Width * scaleFactor));
            int newHeight = Math.Max(1, (int)(original.Height * scaleFactor));

            using SKBitmap resized = Pictures.Resize(original, newWidth, newHeight);
            byte[] compressed = Pictures.Encode(resized, SKEncodedImageFormat.Jpeg, RESIZED_JPEG_QUALITY);

            if (compressed.Length <= targetSize)
            {
                return compressed;
            }
        }

        throw new IniaAiInputException("Failed to reduce image file size to 5MB or less. Please reduce image resolution or quality and try again.");
    }
}

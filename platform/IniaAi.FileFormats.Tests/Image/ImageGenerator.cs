using SkiaSharp;

namespace IniaAi.FileFormats.Tests.Images;

/// <summary>
/// Generates JPEG images of approximately specified file sizes for testing purposes.
/// </summary>
public class ImageGenerator
{
    private static readonly Random _random = new();

    /// <summary>
    /// Generates a JPEG image with approximately the specified file size.
    /// </summary>
    /// <param name="targetSizeBytes">Target file size in bytes</param>
    /// <returns>MemoryStream containing the generated JPEG image</returns>
    public static MemoryStream GenerateJpegImage(int targetSizeBytes)
    {
        if (targetSizeBytes <= 0)
        {
            throw new ArgumentException("Target size must be greater than 0", nameof(targetSizeBytes));
        }

        // Estimate initial dimensions and quality based on target size
        (int width, int height, int quality) = EstimateImageParameters(targetSizeBytes);

        var memoryStream = new MemoryStream();
        int attempts = 0;
        const int maxAttempts = 10;

        while (attempts < maxAttempts)
        {
            memoryStream.SetLength(0);
            memoryStream.Position = 0;

            // Generate image with random noise pattern
            var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
            using (SKImage image = SKImage.FromPixelCopy(info, RandomOpaquePixels(width, height)))
            using (SKData jpeg = image.Encode(SKEncodedImageFormat.Jpeg, quality))
            {
                jpeg.SaveTo(memoryStream);
            }

            long currentSize = memoryStream.Length;
            long sizeDifference = Math.Abs(currentSize - targetSizeBytes);
            double tolerance = targetSizeBytes * 0.1; // 10% tolerance

            // If we're within tolerance, return the result
            if (sizeDifference <= tolerance)
            {
                memoryStream.Position = 0;
                return memoryStream;
            }

            // Adjust parameters for next attempt
            if (currentSize < targetSizeBytes)
            {
                // Image too small - increase quality or dimensions
                if (quality < 95)
                {
                    quality = Math.Min(100, quality + 10);
                }
                else
                {
                    width = (int)(width * 1.1);
                    height = (int)(height * 1.1);
                }
            }
            else
            {
                // Image too large - decrease quality or dimensions
                if (quality > 15)
                {
                    quality = Math.Max(10, quality - 10);
                }
                else
                {
                    width = (int)(width * 0.9);
                    height = (int)(height * 0.9);
                }
            }

            attempts++;
        }

        // Return the closest result after max attempts
        memoryStream.Position = 0;
        return memoryStream;
    }

    private static (int width, int height, int quality) EstimateImageParameters(int targetSizeBytes)
    {
        // Rough estimation based on typical JPEG compression ratios
        // These values are starting points and will be adjusted
        int estimatedPixels = targetSizeBytes * 8; // Rough estimate
        int dimension = (int)Math.Sqrt(estimatedPixels);

        // Ensure minimum viable dimensions
        int width = Math.Max(100, dimension);
        int height = Math.Max(100, dimension);

        // Start with medium quality
        int quality = 75;

        // Adjust for very small or very large target sizes
        if (targetSizeBytes < 5000) // < 5KB
        {
            width = Math.Min(width, 200);
            height = Math.Min(height, 200);
            quality = 50;
        }
        else if (targetSizeBytes > 1000000) // > 1MB
        {
            quality = 85;
        }

        return (width, height, quality);
    }

    private static byte[] RandomOpaquePixels(int width, int height)
    {
        byte[] pixels = new byte[width * height * 4];
        _random.NextBytes(pixels);
        for (int alpha = 3; alpha < pixels.Length; alpha += 4)
        {
            pixels[alpha] = byte.MaxValue;
        }

        return pixels;
    }
}

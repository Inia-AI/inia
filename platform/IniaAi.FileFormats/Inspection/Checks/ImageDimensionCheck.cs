using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.FileFormats.Images;
using System.Globalization;

namespace IniaAi.FileFormats.Inspection.Checks;

/// <summary>
/// Reads the image header only. The pixel count bounds the memory a decode would take, which is
/// the bomb an image can be.
/// </summary>
public sealed class ImageDimensionCheck : IFileContentCheck
{
    private const string GROUPED = "N0";
    public FileInspection? Run(FileInspectionContext context)
    {
        DetectedContent detected = context.RequireDetected();
        if (detected.PlatformMediaType is not (MediaType.ImageJpeg or MediaType.ImagePng))
        {
            return null;
        }

        if (Pictures.ReadSize(context.Bytes) is not PictureSize info)
        {
            return FileInspection.Refuse(IntakeRefusalReasons.MALFORMED_FILE, "The image is damaged and cannot be opened. Save it again, then upload the new copy.", detected);
        }

        long pixels = (long)info.Width * info.Height;
        if (pixels > context.Limits.MaxImagePixels)
        {
            return FileInspection.Refuse(
                IntakeRefusalReasons.STRUCTURAL_LIMIT,
                $"The image is {info.Width.ToString(GROUPED, CultureInfo.InvariantCulture)} by {info.Height.ToString(GROUPED, CultureInfo.InvariantCulture)} pixels, more than the limit of {context.Limits.MaxImagePixels.ToString(GROUPED, CultureInfo.InvariantCulture)} pixels. Make it smaller, then upload it again.",
                detected);
        }

        return null;
    }
}

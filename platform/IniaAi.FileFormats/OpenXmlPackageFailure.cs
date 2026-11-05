using DocumentFormat.OpenXml.Packaging;

namespace IniaAi.FileFormats;

/// <summary>The exceptions Open XML SDK throws for bytes that are no readable package of the kind asked for.</summary>
internal static class OpenXmlPackageFailure
{
    public static bool IsUnreadablePackage(Exception exception)
    {
        return exception is IOException or InvalidDataException or OpenXmlPackageException or FileFormatException or ArgumentException or InvalidOperationException;
    }
}

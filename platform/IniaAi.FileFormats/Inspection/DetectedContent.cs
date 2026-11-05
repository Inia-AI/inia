using IniaAi.Common;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;

namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// What the bytes say a file is. <paramref name="IanaMediaType"/> is the detector's name for
/// the type; <paramref name="PlatformMediaType"/> is the platform's enum value for the same
/// string when one exists. Whether the platform reads that type is the admission check's
/// question, not this record's.
/// </summary>
public sealed record DetectedContent(string IanaMediaType, MediaType? PlatformMediaType, bool HasMacros)
{
    public static readonly DetectedContent Unrecognised = new(MediaType.ApplicationOctetStream.GetEnumMember(), null, false);

    public static DetectedContent FromIanaMediaType(string ianaMediaType, bool hasMacros)
    {
        return new DetectedContent(
            ianaMediaType,
            Utils.TryParseEnumFromEnumMemberAttribute(ianaMediaType, out MediaType mediaType) ? mediaType : null,
            hasMacros);
    }

    public static DetectedContent FromPlatformMediaType(MediaType mediaType)
    {
        return new DetectedContent(mediaType.GetEnumMember(), mediaType, false);
    }
}

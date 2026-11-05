using IniaAi.Common;
using IniaAi.Common.Enums;
using System.Text;
using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.Inspection.Checks;

/// <summary>
/// A text file has no signature, so the test is negative: no NUL byte and almost no control
/// characters in the first 64 KiB, walked as bytes so junk costs no allocation. Encoding is not
/// validated on purpose: the readers downstream decode with replacement, so a legacy Windows
/// code page that works today must still pass. A UTF-16 sample with a byte-order mark is
/// transcoded to UTF-8 first, because its ASCII characters carry NUL high bytes; without the
/// mark those NULs make it unrecognised. Mail needs a header block that ends in a
/// blank line and names a sender (<c>From</c> or <c>Received</c>), so a CSV whose first line
/// happens to read <c>Date:...</c> stays a CSV.
/// </summary>
internal static partial class TextContentSniffer
{
    private const int SAMPLE_BYTES = 64 * 1024;
    private const double MAX_CONTROL_RATIO = 0.02;
    private const byte HEADER_SEPARATOR = (byte)':';

    private static readonly byte[] _utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly byte[] _utf16LittleEndianBom = [0xFF, 0xFE];
    private static readonly byte[] _utf16BigEndianBom = [0xFE, 0xFF];

    /// <summary>
    /// Whether <paramref name="mediaType"/> is one this sniff yields. Such a type has no
    /// signature: its bytes are recognised by the absence of binary content, not by a mark.
    /// </summary>
    internal static bool Yields(MediaType mediaType)
    {
        return mediaType is MediaType.MessageRfc822 or MediaType.TextPlain or MediaType.TextCsv or MediaType.TextMarkdown;
    }

    internal static DetectedContent? Sniff(byte[] bytes, string fileName)
    {
        ReadOnlySpan<byte> sample = AsUtf8(bytes.AsSpan(0, Math.Min(bytes.Length, SAMPLE_BYTES)));
        if (!LooksLikeText(sample))
        {
            return null;
        }

        if (LooksLikeMailMessage(sample))
        {
            return DetectedContent.FromPlatformMediaType(MediaType.MessageRfc822);
        }

        MediaType byExtension = Utils.DetermineMediaType(fileName);
        return DetectedContent.FromPlatformMediaType(byExtension is MediaType.TextCsv or MediaType.TextMarkdown ? byExtension : MediaType.TextPlain);
    }

    private static ReadOnlySpan<byte> AsUtf8(ReadOnlySpan<byte> sample)
    {
        if (sample.StartsWith(_utf16LittleEndianBom))
        {
            return Encoding.UTF8.GetBytes(Encoding.Unicode.GetString(sample[_utf16LittleEndianBom.Length..]));
        }

        if (sample.StartsWith(_utf16BigEndianBom))
        {
            return Encoding.UTF8.GetBytes(Encoding.BigEndianUnicode.GetString(sample[_utf16BigEndianBom.Length..]));
        }

        return sample.StartsWith(_utf8Bom) ? sample[_utf8Bom.Length..] : sample;
    }

    private static bool LooksLikeText(ReadOnlySpan<byte> sample)
    {
        if (sample.IsEmpty)
        {
            return false;
        }

        int controls = 0;
        foreach (byte b in sample)
        {
            if (b == 0)
            {
                return false;
            }

            if (b < (byte)' ' && b is not ((byte)'\t' or (byte)'\n' or (byte)'\r' or (byte)'\f' or (byte)'\v' or 0x1B))
            {
                controls++;
            }
        }

        return controls <= sample.Length * MAX_CONTROL_RATIO;
    }

    private static bool LooksLikeMailMessage(ReadOnlySpan<byte> sample)
    {
        if (!StartsWithHeaderField(sample))
        {
            return false;
        }

        int lineFeedBlank = sample.IndexOf("\n\n"u8);
        int carriageReturnBlank = sample.IndexOf("\r\n\r\n"u8);
        int headerEnd = lineFeedBlank < 0 || (carriageReturnBlank >= 0 && carriageReturnBlank < lineFeedBlank) ? carriageReturnBlank : lineFeedBlank;
        return headerEnd >= 0 && MailHeaderName().IsMatch(Encoding.Latin1.GetString(sample[..headerEnd]));
    }

    private static bool StartsWithHeaderField(ReadOnlySpan<byte> sample)
    {
        int separator = sample.IndexOf(HEADER_SEPARATOR);
        if (separator <= 0)
        {
            return false;
        }

        foreach (byte b in sample[..separator])
        {
            if (b is < (byte)'!' or > (byte)'~')
            {
                return false;
            }
        }

        return true;
    }

    [GeneratedRegex(@"^(From|Received):", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex MailHeaderName();
}

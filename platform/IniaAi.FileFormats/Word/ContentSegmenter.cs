using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.Word;

public enum SegmentType
{
    Text,
    Image,
    Table
}

public record ContentSegment(SegmentType Type, string Content);

public static partial class ContentSegmenter
{
    [GeneratedRegex(@"(<table\b[^>]*>.*?</table>)|(<img\b[^>]*?\bsrc\s*=\s*[""']data:image[^>]*>)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex ElementRegex();

    public static List<ContentSegment> SegmentContent(string content)
    {
        var segments = new List<ContentSegment>();
        int currentIndex = 0;

        foreach (Match match in ElementRegex().Matches(content))
        {
            // Add text before the match if any
            if (match.Index > currentIndex)
            {
                string text = content[currentIndex..match.Index].Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    segments.Add(new ContentSegment(SegmentType.Text, text));
                }
            }

            // Determine type and add the element
            string element = match.Value;
            if (element.StartsWith("<table", StringComparison.OrdinalIgnoreCase))
            {
                segments.Add(new ContentSegment(SegmentType.Table, element));
            }
            else if (element.StartsWith("<img", StringComparison.OrdinalIgnoreCase))
            {
                segments.Add(new ContentSegment(SegmentType.Image, element));
            }

            currentIndex = match.Index + match.Length;
        }

        // Add remaining text if any
        if (currentIndex < content.Length)
        {
            string text = content[currentIndex..].Trim();
            if (!string.IsNullOrEmpty(text))
            {
                segments.Add(new ContentSegment(SegmentType.Text, text));
            }
        }

        return segments;
    }
}

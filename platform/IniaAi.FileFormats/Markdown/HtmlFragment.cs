using System.Net;
using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.Markdown;

/// <summary>
/// Reads report HTML into a node tree and forgives it the way a browser does: an element left open
/// ends where the next block or its parent ends, and a stray end tag is dropped, so no text is lost.
/// </summary>
internal static partial class HtmlFragment
{
    private const string END_GROUP = "end";
    private const string START_GROUP = "start";
    private const string ATTRIBUTES_GROUP = "attributes";
    private const string SELF_CLOSING_GROUP = "selfClosing";
    private const string NAME_GROUP = "name";
    private const string VALUE_GROUP = "value";

    /// <summary>Past this depth an element takes no children of its own, so the tree stays shallow enough to walk recursively.</summary>
    private const int MAX_NESTING_DEPTH = 256;

    private static readonly HashSet<string> _paragraph = [HtmlTags.PARAGRAPH];

    [GeneratedRegex(@"<!--.*?-->|<![^>]*>|<\?[^>]*>|</(?<end>[a-zA-Z][a-zA-Z0-9:-]*)\s*>|<(?<start>[a-zA-Z][a-zA-Z0-9:-]*)(?<attributes>(?:\s+[^\s""'>/=]+(?:\s*=\s*(?:""[^""]*""|'[^']*'|[^\s""'=<>`]+))?)*)\s*(?<selfClosing>/?)>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"(?<name>[^\s""'>/=]+)(?:\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s""'=<>`]+)))?")]
    private static partial Regex AttributeRegex();

    public static HtmlNode Parse(string html)
    {
        HtmlNode root = HtmlNode.Element(HtmlTags.ROOT);
        var open = new List<HtmlNode> { root };
        int position = 0;

        while (position < html.Length)
        {
            Match tag = TagRegex().Match(html, position);
            int textEnd = tag.Success ? tag.Index : html.Length;
            if (textEnd > position)
            {
                open[^1].Children.Add(HtmlNode.TextNode(WebUtility.HtmlDecode(html[position..textEnd])));
            }

            if (!tag.Success)
            {
                break;
            }

            position = tag.Index + tag.Length;
            if (tag.Groups[END_GROUP].Success)
            {
                CloseElement(open, tag.Groups[END_GROUP].Value.ToLowerInvariant());
                continue;
            }

            if (!tag.Groups[START_GROUP].Success)
            {
                continue;
            }

            string name = tag.Groups[START_GROUP].Value.ToLowerInvariant();
            if (HtmlTags.RawText.Contains(name))
            {
                position = SkipRawText(html, position, name);
                continue;
            }

            string attributes = tag.Groups[ATTRIBUTES_GROUP].Value;
            bool selfClosing = tag.Groups[SELF_CLOSING_GROUP].Value.Length > 0;
            OpenElement(open, name, attributes.Length == 0 ? null : ReadAttributes(attributes), selfClosing);
        }

        return root;
    }

    private static void OpenElement(List<HtmlNode> open, string name, Dictionary<string, string>? attributes, bool selfClosing)
    {
        CloseElementsEndedBy(open, name);

        HtmlNode element = HtmlNode.Element(name, attributes);
        open[^1].Children.Add(element);
        if (!selfClosing && !HtmlTags.Void.Contains(name) && open.Count < MAX_NESTING_DEPTH)
        {
            open.Add(element);
        }
    }

    private static void CloseElementsEndedBy(List<HtmlNode> open, string name)
    {
        if (HtmlTags.ParagraphEnders.Contains(name))
        {
            CloseNearest(open, _paragraph, HtmlTags.ParagraphScope);
        }

        if (HtmlTags.HeadingLevels.ContainsKey(name) && open[^1].Tag is string current && HtmlTags.HeadingLevels.ContainsKey(current))
        {
            open.RemoveAt(open.Count - 1);
        }

        if (HtmlTags.ImpliedEnds.TryGetValue(name, out (HashSet<string> Siblings, HashSet<string> Scope) end))
        {
            CloseNearest(open, end.Siblings, end.Scope);
        }
    }

    private static void CloseNearest(List<HtmlNode> open, HashSet<string> targets, HashSet<string> scope)
    {
        for (int i = open.Count - 1; i > 0; i--)
        {
            string tag = open[i].Tag ?? string.Empty;
            if (targets.Contains(tag))
            {
                open.RemoveRange(i, open.Count - i);
                return;
            }

            if (scope.Contains(tag))
            {
                return;
            }
        }
    }

    private static void CloseElement(List<HtmlNode> open, string name)
    {
        if (name == HtmlTags.LINE_BREAK)
        {
            open[^1].Children.Add(HtmlNode.Element(HtmlTags.LINE_BREAK));
            return;
        }

        for (int i = open.Count - 1; i > 0; i--)
        {
            if (open[i].Is(name))
            {
                open.RemoveRange(i, open.Count - i);
                return;
            }
        }
    }

    private static int SkipRawText(string html, int position, string name)
    {
        int end = html.IndexOf($"</{name}", position, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return html.Length;
        }

        int close = html.IndexOf('>', end);
        return close < 0 ? html.Length : close + 1;
    }

    private static Dictionary<string, string> ReadAttributes(string text)
    {
        var attributes = new Dictionary<string, string>();
        foreach (Match attribute in AttributeRegex().Matches(text))
        {
            string value = attribute.Groups[VALUE_GROUP].Success ? WebUtility.HtmlDecode(attribute.Groups[VALUE_GROUP].Value) : string.Empty;
            _ = attributes.TryAdd(attribute.Groups[NAME_GROUP].Value.ToLowerInvariant(), value);
        }

        return attributes;
    }
}

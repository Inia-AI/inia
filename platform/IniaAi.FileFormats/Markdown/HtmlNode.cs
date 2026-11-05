namespace IniaAi.FileFormats.Markdown;

internal sealed class HtmlNode
{
    private static readonly Dictionary<string, string> _noAttributes = new();

    private readonly Dictionary<string, string> _attributes;

    private HtmlNode(string? tag, string text, Dictionary<string, string> attributes)
    {
        Tag = tag;
        Text = text;
        _attributes = attributes;
    }

    public string? Tag { get; }

    public string Text { get; }

    public List<HtmlNode> Children { get; } = [];

    public bool IsText => Tag is null;

    public static HtmlNode Element(string tag, Dictionary<string, string>? attributes = null)
    {
        return new HtmlNode(tag, string.Empty, attributes ?? _noAttributes);
    }

    public static HtmlNode TextNode(string text)
    {
        return new HtmlNode(null, text, _noAttributes);
    }

    public string? Attribute(string name)
    {
        return _attributes.TryGetValue(name, out string? value) ? value : null;
    }

    public bool Is(string tag)
    {
        return Tag == tag;
    }

    public string TextContent()
    {
        if (IsText)
        {
            return Text;
        }

        return string.Concat(Children.Select(child => child.TextContent()));
    }
}

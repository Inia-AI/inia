namespace IniaAi.FileFormats.Markdown;

internal static class HtmlTags
{
    public const string ROOT = "#root";
    public const string PARAGRAPH = "p";
    public const string LINE_BREAK = "br";
    public const string LIST_ITEM = "li";
    public const string UNORDERED_LIST = "ul";
    public const string ORDERED_LIST = "ol";
    public const string BLOCKQUOTE = "blockquote";
    public const string PREFORMATTED = "pre";
    public const string HORIZONTAL_RULE = "hr";
    public const string TABLE = "table";
    public const string ROW = "tr";
    public const string CAPTION = "caption";
    public const string CODE = "code";
    public const string INPUT = "input";
    public const string LINK = "a";
    public const string STRONG = "strong";
    public const string BOLD = "b";
    public const string EMPHASIS = "em";
    public const string ITALIC = "i";
    public const string DELETED = "del";
    public const string STRUCK = "s";
    public const string STRIKE = "strike";
    public const string CELL = "td";
    public const string HEADER_CELL = "th";
    public const string TERM = "dt";
    public const string DEFINITION = "dd";
    public const string DEFINITION_LIST = "dl";
    public const string TABLE_HEAD = "thead";
    public const string TABLE_BODY = "tbody";
    public const string TABLE_FOOT = "tfoot";
    public const string HEADING_1 = "h1";
    public const string HEADING_2 = "h2";
    public const string HEADING_3 = "h3";
    public const string HEADING_4 = "h4";
    public const string HEADING_5 = "h5";
    public const string HEADING_6 = "h6";
    public const string IMAGE = "img";
    public const string META = "meta";
    public const string RESOURCE_LINK = "link";

    public static readonly HashSet<string> Void = ["area", "base", LINE_BREAK, "col", "embed", HORIZONTAL_RULE, IMAGE, INPUT, RESOURCE_LINK, META, "source", "track", "wbr"];

    /// <summary>Tags whose content is not text to read: the parser skips it.</summary>
    public static readonly HashSet<string> RawText = ["script", "style"];

    /// <summary>Tags left out of the Markdown with everything inside them: page metadata, and pictures.</summary>
    public static readonly HashSet<string> Dropped = ["head", "template", "noscript", META, RESOURCE_LINK, "svg", IMAGE];

    private static readonly string[] _sectioning = ["address", "article", "aside", "details", "div", "fieldset", "figcaption", "figure", "footer", "form", "header", "main", "nav", "section"];

    public static readonly HashSet<string> Block =
    [
        .. _sectioning, BLOCKQUOTE, "body", CAPTION, "center", DEFINITION, "dialog", DEFINITION_LIST, TERM, HEADING_1, HEADING_2,
        HEADING_3, HEADING_4, HEADING_5, HEADING_6, "hgroup", HORIZONTAL_RULE, "html", LIST_ITEM, ORDERED_LIST, PARAGRAPH, PREFORMATTED,
        "summary", TABLE, TABLE_BODY, CELL, TABLE_FOOT, HEADER_CELL, TABLE_HEAD, ROW, UNORDERED_LIST,
    ];

    public static readonly Dictionary<string, int> HeadingLevels = new()
    {
        [HEADING_1] = 1,
        [HEADING_2] = 2,
        [HEADING_3] = 3,
        [HEADING_4] = 4,
        [HEADING_5] = 5,
        [HEADING_6] = 6,
    };

    public static readonly HashSet<string> Cells = [CELL, HEADER_CELL];

    /// <summary>A start tag of one of these ends an open paragraph, as in a browser.</summary>
    public static readonly HashSet<string> ParagraphEnders =
    [
        .. _sectioning, BLOCKQUOTE, DEFINITION, DEFINITION_LIST, TERM, HEADING_1, HEADING_2, HEADING_3, HEADING_4, HEADING_5, HEADING_6,
        HORIZONTAL_RULE, LIST_ITEM, ORDERED_LIST, PARAGRAPH, PREFORMATTED, TABLE, UNORDERED_LIST,
    ];

    public static readonly HashSet<string> ParagraphScope = [TABLE, CELL, HEADER_CELL, CAPTION];

    /// <summary>
    /// A start tag that ends an open sibling of the same kind, as a new list item ends the last one;
    /// the search stops at the scope, so an item of an outer list stays open.
    /// </summary>
    public static readonly Dictionary<string, (HashSet<string> Siblings, HashSet<string> Scope)> ImpliedEnds = BuildImpliedEnds();

    private static Dictionary<string, (HashSet<string> Siblings, HashSet<string> Scope)> BuildImpliedEnds()
    {
        HashSet<string> listItem = [LIST_ITEM];
        HashSet<string> definitions = [TERM, DEFINITION];
        HashSet<string> row = [ROW];
        HashSet<string> sections = [TABLE_HEAD, TABLE_BODY, TABLE_FOOT];
        HashSet<string> tableScope = [TABLE];

        return new()
        {
            [LIST_ITEM] = (listItem, [UNORDERED_LIST, ORDERED_LIST]),
            [TERM] = (definitions, [DEFINITION_LIST]),
            [DEFINITION] = (definitions, [DEFINITION_LIST]),
            [ROW] = (row, tableScope),
            [CELL] = (Cells, [ROW, TABLE]),
            [HEADER_CELL] = (Cells, [ROW, TABLE]),
            [TABLE_HEAD] = (sections, tableScope),
            [TABLE_BODY] = (sections, tableScope),
            [TABLE_FOOT] = (sections, tableScope),
        };
    }
}

using System.Text;
using System.Text.RegularExpressions;
using IniaAi.Common.Text;

namespace IniaAi.FileFormats.Markdown;

/// <summary>Turns report HTML into Markdown blocks.</summary>
/// <remarks>
/// An image is left out: the export turned each linked image into its alternative text before this,
/// so only an embedded picture is left. A tag HTML does not know is transparent: its content stays.
/// </remarks>
internal sealed partial class HtmlToMarkdown
{
    private const int MAX_CELL_SPAN = 1000;
    private const string CHECKBOX_TYPE = "checkbox";
    private const string LANGUAGE_CLASS_PREFIX = "language-";
    private const string HREF_ATTRIBUTE = "href";
    private const string START_ATTRIBUTE = "start";
    private const string CLASS_ATTRIBUTE = "class";
    private const string TYPE_ATTRIBUTE = "type";
    private const string CHECKED_ATTRIBUTE = "checked";
    private const string COLSPAN_ATTRIBUTE = "colspan";
    private const string ROWSPAN_ATTRIBUTE = "rowspan";
    private const string ANGLE_OPEN = "<";
    private const string ANGLE_CLOSE = ">";
    private const string ANGLE_OPEN_ENCODED = "%3C";
    private const string ANGLE_CLOSE_ENCODED = "%3E";

    private static readonly char[] _destinationCharactersToWrap = [' ', '(', ')', '<', '>'];

    private readonly int _headingOffset;
    private readonly HashSet<string> _tableBlocks = new(ReferenceEqualityComparer.Instance);
    private int _tableCellDepth;

    private HtmlToMarkdown(int headingOffset)
    {
        _headingOffset = headingOffset;
    }

    [GeneratedRegex(@"`+")]
    private static partial Regex BacktickRunRegex();

    /// <param name="headingOffset">Levels added to every heading in the HTML, so it nests under the report's own headings.</param>
    public static IReadOnlyList<string> ToBlocks(string html, int headingOffset)
    {
        return new HtmlToMarkdown(headingOffset).Blocks(HtmlFragment.Parse(html));
    }

    private List<string> Blocks(HtmlNode container)
    {
        var blocks = new List<string>();
        var inline = new StringBuilder();

        foreach (HtmlNode child in container.Children)
        {
            if (IsDropped(child))
            {
                continue;
            }

            if (IsBlock(child))
            {
                AddParagraph(inline, blocks);
                blocks.AddRange(Block(child));
            }
            else
            {
                _ = inline.Append(Inline(child));
            }
        }

        AddParagraph(inline, blocks);
        return blocks;
    }

    private static bool IsBlock(HtmlNode node)
    {
        return node.Tag is string tag && HtmlTags.Block.Contains(tag);
    }

    private static bool IsDropped(HtmlNode node)
    {
        return node.Tag is string tag && HtmlTags.Dropped.Contains(tag);
    }

    private void AddParagraph(StringBuilder inline, List<string> blocks)
    {
        string paragraph = Paragraph(inline.ToString(), escapeLineStarts: _tableCellDepth == 0);
        _ = inline.Clear();
        if (paragraph.Length > 0)
        {
            blocks.Add(paragraph);
        }
    }

    /// <summary>
    /// Ends each line with a hard break and starts a new paragraph at an empty line. A table cell
    /// cannot open a block, so its lines need no line-start escape.
    /// </summary>
    private static string Paragraph(string inline, bool escapeLineStarts)
    {
        var paragraphs = new List<string>();
        var lines = new List<string>();

        foreach (string rawLine in Emphasis.Resolve(inline).Split(MarkdownText.NEWLINE))
        {
            string line = MarkdownText.CollapseWhitespace(rawLine).Trim();
            if (line.Length > 0)
            {
                lines.Add(escapeLineStarts ? MarkdownText.EscapeLineStart(line) : line);
                continue;
            }

            AddLines(lines, paragraphs);
        }

        AddLines(lines, paragraphs);
        return string.Join(MarkdownText.BLOCK_SEPARATOR, paragraphs);
    }

    private static void AddLines(List<string> lines, List<string> paragraphs)
    {
        if (lines.Count > 0)
        {
            paragraphs.Add(string.Join(MarkdownText.HARD_BREAK, lines));
            lines.Clear();
        }
    }

    private List<string> Block(HtmlNode node)
    {
        return node.Tag switch
        {
            string tag when HtmlTags.HeadingLevels.TryGetValue(tag, out int level) => Heading(node, level),
            HtmlTags.UNORDERED_LIST => List(node, ordered: false),
            HtmlTags.ORDERED_LIST => List(node, ordered: true),
            HtmlTags.BLOCKQUOTE => Quote(node),
            HtmlTags.PREFORMATTED => [_tableCellDepth > 0 ? CodeLines(node) : CodeBlock(node)],
            HtmlTags.HORIZONTAL_RULE => [MarkdownText.HORIZONTAL_RULE],
            HtmlTags.TABLE => Table(node),
            _ => Blocks(node),
        };
    }

    private List<string> Heading(HtmlNode node, int level)
    {
        string text = Emphasis.Resolve(InlineChildren(node));
        return string.IsNullOrWhiteSpace(text) ? [] : [MarkdownText.Heading(level + _headingOffset, text)];
    }

    private List<string> List(HtmlNode node, bool ordered)
    {
        int number = ordered && int.TryParse(node.Attribute(START_ATTRIBUTE), out int start) ? start : 1;
        List<HtmlNode> children = [.. node.Children.Where(child => !child.IsText || !string.IsNullOrWhiteSpace(child.Text))];
        bool loose = children.Exists(child => child.Is(HtmlTags.LIST_ITEM) && child.Children.Exists(grandchild => grandchild.Is(HtmlTags.PARAGRAPH)));
        var items = new List<(string Marker, List<string> Blocks)>();

        foreach (HtmlNode child in children)
        {
            if (!child.Is(HtmlTags.LIST_ITEM) && items.Count > 0)
            {
                items[^1].Blocks.AddRange(ListItemBlocks(child));
                continue;
            }

            string marker = ordered ? $"{number}{MarkdownText.ORDERED_SUFFIX}" : MarkdownText.BULLET;
            number++;
            items.Add(($"{marker}{TaskMarker(child)}", ListItemBlocks(child)));
        }

        string itemSeparator = loose ? MarkdownText.BLOCK_SEPARATOR : MarkdownText.LINE_BREAK;
        IEnumerable<string> rendered = items.Select(item =>
        {
            if (item.Blocks.Count == 0)
            {
                return item.Marker.TrimEnd();
            }

            return $"{item.Marker}{Indent(JoinItemBlocks(item.Blocks, loose), item.Marker.Length)}";
        });
        return [string.Join(itemSeparator, rendered)];
    }

    /// <summary>A table needs a blank line on each side, or a tight list item would read it as text of its paragraph.</summary>
    private string JoinItemBlocks(List<string> blocks, bool loose)
    {
        var joined = new StringBuilder(blocks[0]);
        for (int index = 1; index < blocks.Count; index++)
        {
            bool apart = loose || _tableBlocks.Contains(blocks[index]) || _tableBlocks.Contains(blocks[index - 1]);
            _ = joined.Append(apart ? MarkdownText.BLOCK_SEPARATOR : MarkdownText.LINE_BREAK).Append(blocks[index]);
        }

        return joined.ToString();
    }

    private List<string> ListItemBlocks(HtmlNode node)
    {
        if (node.Is(HtmlTags.LIST_ITEM))
        {
            return Blocks(node);
        }

        if (IsBlock(node))
        {
            return Block(node);
        }

        HtmlNode container = HtmlNode.Element(HtmlTags.LIST_ITEM);
        container.Children.Add(node);
        return Blocks(container);
    }

    private static string TaskMarker(HtmlNode item)
    {
        HtmlNode? checkbox = LeadingCheckbox(item);
        if (checkbox is null)
        {
            return string.Empty;
        }

        return checkbox.Attribute(CHECKED_ATTRIBUTE) is null ? MarkdownText.OPEN_TASK : MarkdownText.CHECKED_TASK;
    }

    private static HtmlNode? LeadingCheckbox(HtmlNode node)
    {
        foreach (HtmlNode child in node.Children)
        {
            if (child.IsText)
            {
                if (string.IsNullOrWhiteSpace(child.Text))
                {
                    continue;
                }

                return null;
            }

            if (child.Is(HtmlTags.INPUT))
            {
                return string.Equals(child.Attribute(TYPE_ATTRIBUTE), CHECKBOX_TYPE, StringComparison.OrdinalIgnoreCase) ? child : null;
            }

            return LeadingCheckbox(child);
        }

        return null;
    }

    private static string Indent(string text, int width)
    {
        string indent = new(MarkdownText.SPACE, width);
        string[] lines = text.Split(MarkdownText.NEWLINE);
        return string.Join(MarkdownText.LINE_BREAK, lines.Select((line, index) => index == 0 || line.Length == 0 ? line : $"{indent}{line}"));
    }

    private List<string> Quote(HtmlNode node)
    {
        List<string> blocks = Blocks(node);
        if (blocks.Count == 0)
        {
            return [];
        }

        IEnumerable<string> lines = string.Join(MarkdownText.BLOCK_SEPARATOR, blocks)
            .Split(MarkdownText.NEWLINE)
            .Select(line => line.Length == 0 ? MarkdownText.EMPTY_QUOTE_LINE : $"{MarkdownText.QUOTE_PREFIX}{line}");
        return [string.Join(MarkdownText.LINE_BREAK, lines)];
    }

    private static string CodeBlock(HtmlNode node)
    {
        HtmlNode? code = node.Children.Find(child => child.Is(HtmlTags.CODE));
        string language = (code?.Attribute(CLASS_ATTRIBUTE) ?? string.Empty)
            .Split(MarkdownText.SPACE, StringSplitOptions.RemoveEmptyEntries)
            .Where(name => name.StartsWith(LANGUAGE_CLASS_PREFIX, StringComparison.Ordinal))
            .Select(name => name[LANGUAGE_CLASS_PREFIX.Length..])
            .FirstOrDefault() ?? string.Empty;
        string text = node.TextContent().TrimEnd(MarkdownText.NEWLINE);
        string fence = new(MarkdownText.BACKTICK, Math.Max(MarkdownText.MIN_CODE_FENCE_LENGTH, LongestBacktickRun(text) + 1));
        return $"{fence}{language}{MarkdownText.LINE_BREAK}{text}{MarkdownText.LINE_BREAK}{fence}";
    }

    /// <summary>A table cell cannot hold a code block, so each line of preformatted text becomes code of its own.</summary>
    private static string CodeLines(HtmlNode node)
    {
        IEnumerable<string> lines = node.TextContent()
            .Split(MarkdownText.NEWLINE)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(CodeSpan);
        return string.Join(MarkdownText.LINE_BREAK, lines);
    }

    private List<string> Table(HtmlNode node)
    {
        var blocks = new List<string>();
        HtmlNode? caption = node.Children.Find(child => child.Is(HtmlTags.CAPTION));
        if (caption is not null)
        {
            blocks.AddRange(Blocks(caption));
        }

        List<HtmlNode> rows = [.. Rows(node)];
        List<string?>[] grid = [.. rows.Select(_ => new List<string?>())];
        for (int row = 0; row < rows.Count; row++)
        {
            Fill(grid, rows, row);
        }

        List<IReadOnlyList<string>> cells = [.. grid
            .Where(row => row.Count > 0)
            .Select(row => (IReadOnlyList<string>)[.. row.Select(cell => cell ?? string.Empty)])];
        if (cells.Count > 0)
        {
            string table = MarkdownText.Table(cells);
            _ = _tableBlocks.Add(table);
            blocks.Add(table);
        }

        return blocks;
    }

    private void Fill(List<string?>[] grid, List<HtmlNode> rows, int row)
    {
        int column = 0;

        foreach (HtmlNode cell in rows[row].Children.Where(child => child.Tag is string tag && HtmlTags.Cells.Contains(tag)))
        {
            while (column < grid[row].Count && grid[row][column] is not null)
            {
                column++;
            }

            int columnSpan = Span(cell, COLSPAN_ATTRIBUTE);
            int rowSpan = Math.Min(Span(cell, ROWSPAN_ATTRIBUTE), rows.Count - row);
            for (int spannedRow = 0; spannedRow < rowSpan; spannedRow++)
            {
                for (int spannedColumn = 0; spannedColumn < columnSpan; spannedColumn++)
                {
                    bool origin = spannedRow == 0 && spannedColumn == 0;
                    Set(grid[row + spannedRow], column + spannedColumn, origin ? CellMarkdown(cell) : string.Empty);
                }
            }

            column += columnSpan;
        }
    }

    private static IEnumerable<HtmlNode> Rows(HtmlNode node)
    {
        foreach (HtmlNode child in node.Children)
        {
            if (child.Is(HtmlTags.ROW))
            {
                yield return child;
            }
            else if (!child.IsText && !child.Is(HtmlTags.TABLE) && !child.Is(HtmlTags.CAPTION))
            {
                foreach (HtmlNode row in Rows(child))
                {
                    yield return row;
                }
            }
        }
    }

    private static void Set(List<string?> cells, int column, string value)
    {
        while (cells.Count <= column)
        {
            cells.Add(null);
        }

        cells[column] = value;
    }

    private static int Span(HtmlNode cell, string attribute)
    {
        return int.TryParse(cell.Attribute(attribute), out int span) && span > 1 ? Math.Min(span, MAX_CELL_SPAN) : 1;
    }

    private string CellMarkdown(HtmlNode cell)
    {
        _tableCellDepth++;
        try
        {
            return string.Join(MarkdownText.LINE_BREAK, Blocks(cell));
        }
        finally
        {
            _tableCellDepth--;
        }
    }

    private string Inline(HtmlNode node)
    {
        if (node.IsText)
        {
            return MarkdownText.Escape(MarkdownText.CollapseWhitespace(Emphasis.WithoutMarkers(node.Text)));
        }

        return node.Tag switch
        {
            HtmlTags.LINE_BREAK => MarkdownText.LINE_BREAK,
            HtmlTags.STRONG or HtmlTags.BOLD => Emphasis.Wrap(InlineChildren(node), Emphasis.Kind.Strong),
            HtmlTags.EMPHASIS or HtmlTags.ITALIC => Emphasis.Wrap(InlineChildren(node), Emphasis.Kind.Emphasis),
            HtmlTags.DELETED or HtmlTags.STRUCK or HtmlTags.STRIKE => Emphasis.Wrap(InlineChildren(node), Emphasis.Kind.Strikethrough),
            HtmlTags.CODE => CodeSpan(node.TextContent()),
            HtmlTags.LINK => Link(node),
            HtmlTags.INPUT => string.Empty,
            _ when IsDropped(node) => string.Empty,
            _ when IsBlock(node) => $"{MarkdownText.LINE_BREAK}{InlineChildren(node)}{MarkdownText.LINE_BREAK}",
            _ => InlineChildren(node),
        };
    }

    private string InlineChildren(HtmlNode node)
    {
        return string.Concat(node.Children.Select(Inline));
    }

    /// <summary>Puts the brackets around the text and leaves its outer whitespace outside them, where Markdown needs it.</summary>
    private static string Surround(string inline, string opening, string closing)
    {
        string core = inline.Trim();
        if (core.Length == 0)
        {
            return inline;
        }

        string leading = inline[..(inline.Length - inline.TrimStart().Length)];
        string trailing = inline[inline.TrimEnd().Length..];
        return $"{leading}{opening}{core}{closing}{trailing}";
    }

    private string Link(HtmlNode node)
    {
        string text = InlineChildren(node);
        string href = (node.Attribute(HREF_ATTRIBUTE) ?? string.Empty).Trim();
        if (href.Length == 0 || !LinkAddress.IsSafe(href))
        {
            return text;
        }

        if (node.TextContent().Trim() == href && IsAutolink(href))
        {
            return $"{MarkdownText.AUTOLINK_OPEN}{href}{MarkdownText.AUTOLINK_CLOSE}";
        }

        if (text.Trim().Length == 0)
        {
            text = MarkdownText.Escape(href);
        }

        return Surround(text, MarkdownText.LINK_TEXT_OPEN, $"{MarkdownText.LINK_TEXT_CLOSE}{Destination(href)}{MarkdownText.LINK_CLOSE}");
    }

    private static bool IsAutolink(string href)
    {
        return href.IndexOfAny(_destinationCharactersToWrap) < 0 && Uri.TryCreate(href, UriKind.Absolute, out _);
    }

    private static string Destination(string href)
    {
        if (href.IndexOfAny(_destinationCharactersToWrap) < 0)
        {
            return href;
        }

        string encoded = href
            .Replace(ANGLE_OPEN, ANGLE_OPEN_ENCODED, StringComparison.Ordinal)
            .Replace(ANGLE_CLOSE, ANGLE_CLOSE_ENCODED, StringComparison.Ordinal);
        return $"{MarkdownText.AUTOLINK_OPEN}{encoded}{MarkdownText.AUTOLINK_CLOSE}";
    }

    private static string CodeSpan(string text)
    {
        string content = MarkdownText.CollapseWhitespace(text);
        if (content.Trim().Length == 0)
        {
            return content;
        }

        string fence = new(MarkdownText.BACKTICK, LongestBacktickRun(content) + 1);
        string padding = content.StartsWith(MarkdownText.BACKTICK) || content.EndsWith(MarkdownText.BACKTICK) ? MarkdownText.SPACE.ToString() : string.Empty;
        return $"{fence}{padding}{content}{padding}{fence}";
    }

    private static int LongestBacktickRun(string text)
    {
        return BacktickRunRegex().Matches(text).Select(match => match.Length).DefaultIfEmpty(0).Max();
    }
}

using System.Globalization;
using System.Xml.Linq;

namespace IniaAi.Testing.Ooxml;

/// <summary>
/// A .docx read the way Word shows it: a run's formatting and a paragraph's alignment, indent and list come from the
/// element itself, then its list level, then its styles, then the document defaults. It reads the XML of the package
/// only, so it judges any writer alike, whatever library wrote the file and however it split the text into runs. The
/// contract tests read Word files with it, and so does any test that checks what Word code wrote.
/// </summary>
public sealed class WordFile
{
    private const string NUMBERING_RELATIONSHIP = "/numbering";
    private const string STYLES_RELATIONSHIP = "/styles";
    private const string HYPERLINK_FIELD = "HYPERLINK";
    private const string ANCHOR_SWITCH = "\\l";
    private const string MACRO_PART = "/word/vbaProject.bin";
    private const double WHOLE_IN_FIFTIETHS_OF_A_PERCENT = 5_000;

    private static readonly XNamespace _w = OoxmlPackage.WordprocessingMl;
    private static readonly XNamespace _office = "urn:schemas-microsoft-com:office:office";
    private static readonly XNamespace _vml = "urn:schemas-microsoft-com:vml";
    private static readonly XNamespace _drawing = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";

    private readonly byte[] _document;
    private readonly XElement _body;
    private readonly List<XElement> _paragraphs;
    private readonly Dictionary<string, XElement> _styles;
    private readonly XElement? _defaultParagraphStyle;
    private readonly XElement? _defaultRunProperties;
    private readonly XElement? _defaultParagraphProperties;
    private readonly XElement? _numbering;
    private readonly Dictionary<string, string> _relationshipTargets;
    private readonly Lazy<string> _allText;

    public WordFile(byte[] document)
    {
        _document = document;
        _body = OoxmlPackage.Part(document, OoxmlPackage.DOCUMENT_PART).Root!.Element(_w + "body")!;
        _paragraphs = [.. _body.Descendants(_w + "p").Where(OoxmlPackage.IsShown)];

        IReadOnlyList<(string Id, string Type, string Target)> relationships = OoxmlPackage.PartRelationships(document, OoxmlPackage.DOCUMENT_RELATIONSHIPS);
        _relationshipTargets = relationships.ToDictionary(relationship => relationship.Id, relationship => relationship.Target);
        XElement? styles = OptionalPart(relationships, STYLES_RELATIONSHIP);
        _numbering = OptionalPart(relationships, NUMBERING_RELATIONSHIP);

        _styles = styles is null
            ? []
            : styles.Elements(_w + "style").Where(style => style.Attribute(_w + "styleId") is not null)
                .GroupBy(style => (string)style.Attribute(_w + "styleId")!)
                .ToDictionary(group => group.Key, group => group.First());
        _defaultParagraphStyle = _styles.Values.FirstOrDefault(style => (string?)style.Attribute(_w + "type") == "paragraph" && (string?)style.Attribute(_w + "default") is "1" or "true" or "on");
        _defaultRunProperties = styles?.Element(_w + "docDefaults")?.Element(_w + "rPrDefault")?.Element(_w + "rPr");
        _defaultParagraphProperties = styles?.Element(_w + "docDefaults")?.Element(_w + "pPrDefault")?.Element(_w + "pPr");
        _allText = new Lazy<string>(() => OoxmlPackage.AllText(_document));
    }

    /// <summary>Every paragraph the body shows, those in tables and text boxes included, in document order.</summary>
    public IReadOnlyList<XElement> Paragraphs => _paragraphs;

    /// <summary>The text of the body, one paragraph a line.</summary>
    public string Text => string.Join('\n', Paragraphs.Select(OoxmlPackage.ParagraphText));

    /// <summary>The text of every part: the body, the headers, the footers, the notes and the text boxes.</summary>
    public string AllText => _allText.Value;

    /// <summary>The number of tables in the body, nested ones included.</summary>
    public int Tables => _body.Descendants(_w + "tbl").Count();

    /// <summary>Whether the package carries a macro project.</summary>
    public bool HasMacros => OoxmlPackage.HasPart(_document, MACRO_PART);

    /// <summary>The address of every hyperlink of the body, in document order, from hyperlink elements and fields alike.</summary>
    public IReadOnlyList<string> HyperlinkAddresses
    {
        get
        {
            Dictionary<XElement, string> fieldAddresses = OoxmlPackage.FieldInstructions(_body)
                .Select(code => (code.Start, Address: HyperlinkFieldAddress(code.Instruction)))
                .Where(code => code.Address is not null)
                .ToDictionary(code => code.Start, code => code.Address!);
            return [.. _body.Descendants()
                .Where(OoxmlPackage.IsShown)
                .Select(element => element.Name == _w + "hyperlink" ? LinkTarget(element) : fieldAddresses.GetValueOrDefault(element))
                .OfType<string>()];
        }
    }

    /// <summary>The number of horizontal rules the body draws.</summary>
    public int HorizontalRules => Paragraphs.Count(HasHorizontalRule);

    /// <summary>Whether a drawing floats, so that text can wrap beside it, instead of sitting in a line of text.</summary>
    public bool HasFloatingShape =>
        _body.Descendants(_drawing + "anchor").Any(OoxmlPackage.IsShown)
        || _body.Descendants().Any(element => element.Name.Namespace == _vml && OoxmlPackage.IsShown(element) && ((string?)element.Attribute("style") ?? string.Empty).Contains("position:absolute", StringComparison.OrdinalIgnoreCase));

    /// <summary>The page size and orientation of each section, in section order.</summary>
    public IReadOnlyList<XElement> PageSizes => [.. _body.Descendants(_w + "sectPr").Select(section => section.Element(_w + "pgSz")).OfType<XElement>()];

    /// <summary>The number of pictures and charts the body draws.</summary>
    public int Drawings =>
        _body.Descendants(_w + "drawing").Count(OoxmlPackage.IsShown)
        + _body.Descendants(_w + "pict").Count(picture => OoxmlPackage.IsShown(picture) && !HasHorizontalRule(picture.Ancestors(_w + "p").First()));

    public static string TextOf(XElement paragraph)
    {
        return OoxmlPackage.ParagraphText(paragraph);
    }

    /// <summary>The runs of the paragraph that show text, those inside its hyperlinks and fields included.</summary>
    public static IEnumerable<XElement> RunsOf(XElement paragraph)
    {
        return OoxmlPackage.Runs(paragraph).Where(run => OoxmlPackage.RunText(run).Length > 0);
    }

    public static bool IsInTable(XElement paragraph)
    {
        return paragraph.Ancestors(_w + "tc").Any();
    }

    /// <summary>Whether the paragraph shows nothing: no text, no drawing and no rule.</summary>
    public bool IsBlank(XElement paragraph)
    {
        return TextOf(paragraph).Length == 0
            && !paragraph.Descendants(_w + "drawing").Any()
            && !paragraph.Descendants(_w + "pict").Any()
            && !paragraph.Descendants(_w + "object").Any()
            && !HasHorizontalRule(paragraph);
    }

    /// <summary>Whether the paragraph shows nothing and sits outside a table, as a blank line between blocks does.</summary>
    public bool IsBlankBodyParagraph(XElement paragraph)
    {
        return !IsInTable(paragraph) && IsBlank(paragraph);
    }

    /// <summary>Whether the paragraph breaks a line inside itself instead of starting a new paragraph.</summary>
    public static bool HasLineBreak(XElement paragraph)
    {
        return paragraph.Descendants(_w + "br").Any(lineBreak => (string?)lineBreak.Attribute(_w + "type") is null or "textWrapping")
            || paragraph.Descendants(_w + "cr").Any();
    }

    /// <summary>
    /// Whether the paragraph draws a horizontal rule: a rule shape, or a top or bottom border, its own or its style's, on
    /// a paragraph with no text.
    /// </summary>
    public bool HasHorizontalRule(XElement paragraph)
    {
        bool ruleShape = paragraph.Descendants().Any(element => (string?)element.Attribute(_office + "hr") is "t" or "true");
        bool borderRule = (HasBorder(paragraph, "top") || HasBorder(paragraph, "bottom")) && TextOf(paragraph).Trim().Length == 0;
        return ruleShape || borderRule;
    }

    /// <summary>The width of each column of the table's own grid, in twips.</summary>
    public static IReadOnlyList<int> GridColumns(XElement table)
    {
        return [.. table.Element(_w + "tblGrid")?.Elements(_w + "gridCol").Select(column => OoxmlPackage.Twips(column, "w") ?? 0) ?? []];
    }

    /// <summary>
    /// The width of the table cell that holds the paragraph, in twips, or null outside a table: the grid columns it
    /// spans, or, when the grid gives no widths, its own width, a percentage taken of its table and the page's text.
    /// </summary>
    public static int? CellWidth(XElement paragraph)
    {
        return paragraph.Ancestors(_w + "tc").FirstOrDefault() is { } cell ? WidthOf(cell) ?? 0 : null;
    }

    /// <summary>The paragraphs whose text holds <paramref name="phrase"/>.</summary>
    public IEnumerable<XElement> ParagraphsWith(string phrase)
    {
        return Paragraphs.Where(paragraph => TextOf(paragraph).Contains(phrase, StringComparison.Ordinal));
    }

    /// <summary>The position of the first paragraph whose text holds <paramref name="phrase"/>, or -1.</summary>
    public int IndexOfParagraphWith(string phrase)
    {
        return _paragraphs.FindIndex(paragraph => TextOf(paragraph).Contains(phrase, StringComparison.Ordinal));
    }

    /// <summary>The position of the last paragraph whose text holds <paramref name="phrase"/>, or -1.</summary>
    public int LastIndexOfParagraphWith(string phrase)
    {
        return _paragraphs.FindLastIndex(paragraph => TextOf(paragraph).Contains(phrase, StringComparison.Ordinal));
    }

    /// <summary>Whether some paragraph shows <paramref name="phrase"/> in runs that all meet the test.</summary>
    public bool Shows(string phrase, Func<XElement, bool> runTest)
    {
        return ParagraphsWith(phrase).Any(paragraph => ShowsPhrase(paragraph, phrase, runTest));
    }

    /// <summary>
    /// Whether the paragraph shows <paramref name="phrase"/>, which is not empty, and every run that shows a character of
    /// it meets the test.
    /// </summary>
    public bool ShowsPhrase(XElement paragraph, string phrase, Func<XElement, bool> runTest)
    {
        if (phrase.Length == 0)
        {
            return false;
        }

        List<(XElement Run, string Text)> runs = [.. RunsOf(paragraph).Select(run => (run, OoxmlPackage.RunText(run)))];
        int start = string.Concat(runs.Select(run => run.Text)).IndexOf(phrase, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        int position = 0;
        foreach ((XElement run, string runText) in runs)
        {
            int end = position + runText.Length;
            if (end > start && position < start + phrase.Length && !runTest(run))
            {
                return false;
            }

            position = end;
        }

        return true;
    }

    public bool IsBold(XElement run)
    {
        return Toggle(run, "b");
    }

    public bool IsItalic(XElement run)
    {
        return Toggle(run, "i");
    }

    public bool IsAllCaps(XElement run)
    {
        return Toggle(run, "caps");
    }

    public bool IsStruckThrough(XElement run)
    {
        return Toggle(run, "strike");
    }

    /// <summary>The run's font size in points.</summary>
    public double? FontSize(XElement run)
    {
        return HalfPoints(RunProperty(run, "sz"));
    }

    /// <summary>The run's font, as its Latin text shows it.</summary>
    public string? FontName(XElement run)
    {
        return (string?)RunProperty(run, "rFonts")?.Attribute(_w + "ascii");
    }

    /// <summary>The run's colour as six hexadecimal digits, or null when it is automatic.</summary>
    public string? Colour(XElement run)
    {
        string? colour = (string?)RunProperty(run, "color")?.Attribute(_w + "val");
        return colour is null or "auto" ? null : colour.ToUpperInvariant();
    }

    /// <summary>The name of the paragraph's own style, as Word lists it.</summary>
    public string? StyleName(XElement paragraph)
    {
        return ParagraphStyle(paragraph) is { } style ? (string?)style.Element(_w + "name")?.Attribute(_w + "val") : null;
    }

    /// <summary>The paragraph's alignment as WordprocessingML names it, such as "both" for justified text.</summary>
    public string? Alignment(XElement paragraph)
    {
        return (string?)ParagraphProperty(paragraph, "jc")?.Attribute(_w + "val");
    }

    public bool IsListItem(XElement paragraph)
    {
        return ListLevel(paragraph) is not null;
    }

    /// <summary>Whether the paragraph is an item of a list that counts, not one of bullets.</summary>
    public bool IsNumberedListItem(XElement paragraph)
    {
        return (string?)ListLevel(paragraph)?.Element(_w + "numFmt")?.Attribute(_w + "val") is { } format && format is not "bullet" and not "none";
    }

    /// <summary>The text a list item's marker shows, as its list level defines it, such as "•" or "%1.".</summary>
    public string? ListMarker(XElement paragraph)
    {
        return (string?)ListLevel(paragraph)?.Element(_w + "lvlText")?.Attribute(_w + "val");
    }

    /// <summary>The list level of the paragraph, 0 for the top level.</summary>
    public int ListLevelNumber(XElement paragraph)
    {
        return int.Parse(NumberingValue(paragraph, "ilvl") ?? "0", CultureInfo.InvariantCulture);
    }

    /// <summary>Where a list item's text starts, as its list level sets it, in twips.</summary>
    public int ListTextPosition(XElement paragraph)
    {
        XElement? indent = ListLevel(paragraph)?.Element(_w + "pPr")?.Element(_w + "ind");
        return OoxmlPackage.Twips(indent, "left") ?? OoxmlPackage.Twips(indent, "start") ?? 0;
    }

    /// <summary>
    /// How far from the margin the paragraph's first line, and so a list item's marker, starts, in twips. Each indent
    /// attribute comes from the first of the paragraph, its list level and its styles that sets it.
    /// </summary>
    public int MarkerPosition(XElement paragraph)
    {
        List<XElement> indents = [.. IndentSources(paragraph)];
        int left = indents.Select(indent => OoxmlPackage.Twips(indent, "left") ?? OoxmlPackage.Twips(indent, "start")).FirstOrDefault(value => value is not null) ?? 0;
        XElement? firstLine = indents.FirstOrDefault(indent => OoxmlPackage.Twips(indent, "firstLine") is not null || OoxmlPackage.Twips(indent, "hanging") is not null);
        return left + (OoxmlPackage.Twips(firstLine, "firstLine") ?? 0) - (OoxmlPackage.Twips(firstLine, "hanging") ?? 0);
    }

    /// <summary>The font and size, in points, of a list item's marker.</summary>
    public (string? Font, double? Size) MarkerFont(XElement paragraph)
    {
        IEnumerable<XElement?> sources =
        [
            ListLevel(paragraph)?.Element(_w + "rPr"),
            paragraph.Element(_w + "pPr")?.Element(_w + "rPr"),
            .. StyleChain(ParagraphStyle(paragraph)).Select(style => style.Element(_w + "rPr")),
            _defaultRunProperties,
        ];
        string? font = sources.Select(properties => (string?)properties?.Element(_w + "rFonts")?.Attribute(_w + "ascii")).FirstOrDefault(name => name is not null);
        double? size = sources.Select(properties => HalfPoints(properties?.Element(_w + "sz"))).FirstOrDefault(points => points is not null);
        return (font, size);
    }

    private static int Span(XElement cell)
    {
        return int.Parse((string?)cell.Element(_w + "tcPr")?.Element(_w + "gridSpan")?.Attribute(_w + "val") ?? "1", CultureInfo.InvariantCulture);
    }

    private static int? WidthOf(XElement cell)
    {
        XElement row = cell.Ancestors(_w + "tr").First();
        XElement table = row.Ancestors(_w + "tbl").First();
        int gridBefore = int.Parse((string?)row.Element(_w + "trPr")?.Element(_w + "gridBefore")?.Attribute(_w + "val") ?? "0", CultureInfo.InvariantCulture);
        int start = gridBefore + CellsOf(row).TakeWhile(other => other != cell).Sum(Span);
        int fromGrid = GridColumns(table).Skip(start).Take(Span(cell)).Sum();
        return fromGrid > 0 ? fromGrid : Width(cell.Element(_w + "tcPr")?.Element(_w + "tcW"), () => TableWidth(table));
    }

    /// <summary>The cells of the row, those a content control or custom markup wraps included, but not those of a nested table.</summary>
    private static IEnumerable<XElement> CellsOf(XElement row)
    {
        return row.Descendants(_w + "tc").Where(cell => cell.Ancestors(_w + "tr").First() == row);
    }

    private static int? TableWidth(XElement table)
    {
        return Width(
            table.Element(_w + "tblPr")?.Element(_w + "tblW"),
            () => table.Ancestors(_w + "tc").FirstOrDefault() is { } outerCell ? WidthOf(outerCell) : TextWidth(table));
    }

    /// <summary>A width in twips, or a percentage of <paramref name="whole"/>: in fiftieths of a percent, or written with a percent sign.</summary>
    private static int? Width(XElement? width, Func<int?> whole)
    {
        string? value = (string?)width?.Attribute(_w + "w");
        string? type = (string?)width?.Attribute(_w + "type");
        if (type == "dxa")
        {
            return OoxmlPackage.Twips(width, "w");
        }

        if (type != "pct" || value is null || whole() is not { } total)
        {
            return null;
        }

        double share = value.EndsWith('%')
            ? double.Parse(value.TrimEnd('%'), CultureInfo.InvariantCulture) / 100
            : double.Parse(value, CultureInfo.InvariantCulture) / WHOLE_IN_FIFTIETHS_OF_A_PERCENT;
        return (int)Math.Round(total * share);
    }

    /// <summary>The width between the margins of the section that holds the element, in twips.</summary>
    private static int? TextWidth(XElement element)
    {
        XElement? topLevel = element.AncestorsAndSelf().FirstOrDefault(ancestor => ancestor.Parent?.Name == _w + "body");
        XElement? section = topLevel?.ElementsAfterSelf().Prepend(topLevel)
            .Select(block => block.Name == _w + "sectPr" ? block : block.Element(_w + "pPr")?.Element(_w + "sectPr"))
            .FirstOrDefault(found => found is not null);
        XElement? margins = section?.Element(_w + "pgMar");
        return OoxmlPackage.Twips(section?.Element(_w + "pgSz"), "w")
            - (OoxmlPackage.Twips(margins, "left") ?? OoxmlPackage.Twips(margins, "start") ?? 0)
            - (OoxmlPackage.Twips(margins, "right") ?? OoxmlPackage.Twips(margins, "end") ?? 0);
    }

    private static string? HyperlinkFieldAddress(string instruction)
    {
        string[] parts = instruction.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !string.Equals(parts[0], HYPERLINK_FIELD, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return parts[1] == ANCHOR_SWITCH && parts.Length > 2 ? parts[2].Trim('"') : parts[1].Trim('"');
    }

    private static double? HalfPoints(XElement? size)
    {
        return (string?)size?.Attribute(_w + "val") is { } value ? double.Parse(value, CultureInfo.InvariantCulture) / 2 : null;
    }

    private XElement? OptionalPart(IReadOnlyList<(string Id, string Type, string Target)> relationships, string relationshipType)
    {
        return OoxmlPackage.FindTargetByType(relationships, OoxmlPackage.DOCUMENT_FOLDER, relationshipType) is { } part
            ? OoxmlPackage.Part(_document, part).Root
            : null;
    }

    private string? LinkTarget(XElement hyperlink)
    {
        return (string?)hyperlink.Attribute(OoxmlPackage.Relationships + "id") is { } id && _relationshipTargets.TryGetValue(id, out string? target)
            ? target
            : (string?)hyperlink.Attribute(_w + "anchor");
    }

    private bool Toggle(XElement run, string property)
    {
        return RunProperty(run, property) is { } toggle && (string?)toggle.Attribute(_w + "val") is null or "1" or "true" or "on";
    }

    private XElement? RunProperty(XElement run, string property)
    {
        XElement? own = run.Element(_w + "rPr")?.Element(_w + property);
        if (own is not null)
        {
            return own;
        }

        XElement? characterStyle = (string?)run.Element(_w + "rPr")?.Element(_w + "rStyle")?.Attribute(_w + "val") is { } id ? _styles.GetValueOrDefault(id) : null;
        XElement paragraph = run.Ancestors(_w + "p").First();
        return StyleChain(characterStyle).Concat(StyleChain(ParagraphStyle(paragraph)))
            .Select(style => style.Element(_w + "rPr")?.Element(_w + property))
            .Append(_defaultRunProperties?.Element(_w + property))
            .FirstOrDefault(found => found is not null);
    }

    private XElement? ParagraphProperty(XElement paragraph, string property)
    {
        return ParagraphPropertySources(paragraph, property).FirstOrDefault();
    }

    /// <summary>Each setting of the property that reaches the paragraph, the nearest first: its own, its list level's, its styles' and the default.</summary>
    private IEnumerable<XElement> ParagraphPropertySources(XElement paragraph, string property)
    {
        XElement?[] sources =
        [
            paragraph.Element(_w + "pPr")?.Element(_w + property),
            ListLevel(paragraph)?.Element(_w + "pPr")?.Element(_w + property),
            .. StyleChain(ParagraphStyle(paragraph)).Select(style => style.Element(_w + "pPr")?.Element(_w + property)),
            _defaultParagraphProperties?.Element(_w + property),
        ];
        return sources.OfType<XElement>();
    }

    /// <summary>Whether the nearest setting of the paragraph's border on that side draws a line.</summary>
    private bool HasBorder(XElement paragraph, string side)
    {
        XElement? border = ParagraphPropertySources(paragraph, "pBdr").Select(borders => borders.Element(_w + side)).FirstOrDefault(found => found is not null);
        return border is not null && (string?)border.Attribute(_w + "val") is not ("nil" or "none");
    }

    private IEnumerable<XElement> IndentSources(XElement paragraph)
    {
        XElement?[] sources =
        [
            paragraph.Element(_w + "pPr")?.Element(_w + "ind"),
            ListLevel(paragraph)?.Element(_w + "pPr")?.Element(_w + "ind"),
            .. StyleChain(ParagraphStyle(paragraph)).Select(style => style.Element(_w + "pPr")?.Element(_w + "ind")),
        ];
        return sources.OfType<XElement>();
    }

    private XElement? ParagraphStyle(XElement paragraph)
    {
        return (string?)paragraph.Element(_w + "pPr")?.Element(_w + "pStyle")?.Attribute(_w + "val") is { } id
            ? _styles.GetValueOrDefault(id)
            : _defaultParagraphStyle;
    }

    private IEnumerable<XElement> StyleChain(XElement? style)
    {
        HashSet<XElement> seen = [];
        while (style is not null && seen.Add(style))
        {
            yield return style;
            style = (string?)style.Element(_w + "basedOn")?.Attribute(_w + "val") is { } basedOn ? _styles.GetValueOrDefault(basedOn) : null;
        }
    }

    /// <summary>The list or level the paragraph names, from its own numbering or else its styles', each read on its own.</summary>
    private string? NumberingValue(XElement paragraph, string property)
    {
        XElement?[] sources =
        [
            paragraph.Element(_w + "pPr")?.Element(_w + "numPr"),
            .. StyleChain(ParagraphStyle(paragraph)).Select(style => style.Element(_w + "pPr")?.Element(_w + "numPr")),
        ];
        return sources.Select(numbering => (string?)numbering?.Element(_w + property)?.Attribute(_w + "val")).FirstOrDefault(value => value is not null);
    }

    private XElement? ListLevel(XElement paragraph)
    {
        string? numberingId = NumberingValue(paragraph, "numId");
        if (_numbering is null || numberingId is null or "0")
        {
            return null;
        }

        string level = NumberingValue(paragraph, "ilvl") ?? "0";
        XElement? instance = NumberingInstance(numberingId);
        XElement? overridden = instance?.Elements(_w + "lvlOverride")
            .FirstOrDefault(element => (string?)element.Attribute(_w + "ilvl") == level)?.Element(_w + "lvl");
        return overridden ?? AbstractNumbering(instance)?
            .Elements(_w + "lvl")
            .FirstOrDefault(element => (string?)element.Attribute(_w + "ilvl") == level);
    }

    private XElement? NumberingInstance(string numberingId)
    {
        return _numbering?.Elements(_w + "num").FirstOrDefault(element => (string?)element.Attribute(_w + "numId") == numberingId);
    }

    /// <summary>The list definition that holds the instance's levels, through the numbering style it links to when it has none of its own.</summary>
    private XElement? AbstractNumbering(XElement? instance)
    {
        HashSet<XElement> seen = [];
        while (instance is not null && seen.Add(instance))
        {
            string? abstractId = (string?)instance.Element(_w + "abstractNumId")?.Attribute(_w + "val");
            XElement? definition = _numbering?.Elements(_w + "abstractNum").FirstOrDefault(element => (string?)element.Attribute(_w + "abstractNumId") == abstractId);
            if ((string?)definition?.Element(_w + "numStyleLink")?.Attribute(_w + "val") is not { } linkedStyle)
            {
                return definition;
            }

            string? linkedId = (string?)_styles.GetValueOrDefault(linkedStyle)?.Element(_w + "pPr")?.Element(_w + "numPr")?.Element(_w + "numId")?.Attribute(_w + "val");
            instance = linkedId is null ? null : NumberingInstance(linkedId);
        }

        return null;
    }
}

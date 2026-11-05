using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using HtmlToOpenXml;
using IniaAi.Common.Interfaces;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using Microsoft.Extensions.Logging;
using System.Globalization;
using System.Text.RegularExpressions;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>One report export in progress. Sizes are in points unless a name says otherwise.</summary>
internal sealed partial class OpenXmlReport
{
    private const string BODY_FONT = "Calibri";
    private const int BODY_HALF_POINTS = 22;
    private const int HEADING_HALF_POINTS = 28;
    private const int REPORT_TITLE_HALF_POINTS = 40;
    private const int FOOTER_HALF_POINTS = 18;
    private const int SPACE_AFTER_TWIPS = 240;
    private const int DEFAULT_SPACE_AFTER_TWIPS = 160;
    private const int DEFAULT_LINE_TWIPS = 259;
    private const int SCORECARD_HALF_POINTS = 24;
    private const string SCORECARD_DRIVER_PREFIX = "Scorecard Driver: ";
    private const int BANNER_SPACE_AFTER_TWIPS = 360;
    private const uint RULE_EIGHTHS_OF_A_POINT = 12;
    private const string RULE_COLOR = "C7C7C7";
    private const string SUBTITLE_COLOR = "365F91";
    private const string REPORT_TITLE = "CREDIT ANALYSIS REPORT";
    private const string NOT_AVAILABLE = "N/A";
    private const string LANDSCAPE = "Landscape";
    private const string LETTER = "Letter";
    private const string RATING_KEY = "Rating";
    private const string HEADING_STYLE = "Heading";
    private const int MARGIN_TOP_BOTTOM_TWIPS = 720;
    private const int MARGIN_SIDES_TWIPS = 1080;
    private const int HEADER_FOOTER_TWIPS = 360;
    private const double CHART_ASPECT = 0.6;
    private const double CHART_FLOAT_ASPECT = 0.66;
    private const double SCREEN_PIXELS_PER_POINT = 96.0 / 72.0;
    private const int MAX_GRID_COLUMNS = 12;
    private const int LIST_STEP_TWIPS = 360;
    private const string LIST_PARAGRAPH_STYLE = "ListParagraph";

    private readonly MainDocumentPart _main;
    private readonly Document _wordDocument;
    private readonly Body _body;
    private readonly HtmlConverter _html;
    private readonly OpenXmlPictures _pictures;
    private readonly IChartImageRenderService? _charts;
    private readonly ReportLayout? _layout;
    private readonly string? _reportContext;
    private readonly string? _footerNotice;
    private readonly ILogger _logger;
    private readonly double _pageWidth;

    public OpenXmlReport(
        WordprocessingDocument document,
        IChartImageRenderService? charts,
        ReportLayout? layout,
        string? reportContext,
        string? footerNotice,
        ILogger logger)
    {
        _main = document.AddMainDocumentPart();
        _body = new Body();
        _wordDocument = new Document(_body);
        _main.Document = _wordDocument;
        OpenXmlParts.AddDefaultStyle(_main, BODY_FONT, BODY_HALF_POINTS, DEFAULT_SPACE_AFTER_TWIPS, DEFAULT_LINE_TWIPS);
        AddSettings();
        _html = new HtmlConverter(_main, new NoDownloads()) { ImageProcessing = ImageProcessingMode.EmbedDataUriOnly };
        _pictures = new OpenXmlPictures(_main);
        _charts = charts;
        _layout = layout;
        _pageWidth = (PageSize(layout).Width - (2.0 * MARGIN_SIDES_TWIPS)) / OpenXmlParts.TWIPS_PER_POINT;
        _reportContext = reportContext;
        _footerNotice = footerNotice;
        _logger = logger;
    }

    public void Build(IDocumentElement root)
    {
        SectionProperties section = PageSetup();
        _ = section.PrependChild(new FooterReference
        {
            Type = HeaderFooterValues.Default,
            Id = OpenXmlParts.AddPageOfPagesFooter(_main, JustificationValues.Center, FOOTER_HALF_POINTS, _footerNotice),
        });
        AddReportHeader(root.Metadata);

        foreach (IDocumentElement child in root.Children)
        {
            Process(child, _body, _pageWidth);
        }

        SplitParagraphsOnLineBreaks();
        JustifyBodyParagraphs();
        RemoveBlankParagraphsBetweenContent();
        SetListsLikeTheBody();
        NumberThePictures();
        _body.Append(section);
        _wordDocument.Save();
    }

    private void Process(IDocumentElement element, OpenXmlCompositeElement target, double width)
    {
        if (element.Type is DocumentElementType.Section
            && element.OutputData?.OfType<GridLayoutOutput>().FirstOrDefault() is { Mode: GridLayoutOutput.GRID_MODE } grid)
        {
            if (string.IsNullOrWhiteSpace(element.Content) && !element.Children.Any())
            {
                return;
            }

            AddSectionHeading(element, target);
            AddGrid(element, grid, target, width);
            return;
        }

        switch (element.Type)
        {
            case DocumentElementType.Section:
                bool hasContent = !string.IsNullOrWhiteSpace(element.Content)
                    || element.Children.Any()
                    || element.OutputData?.Any(output => output is not GridLayoutOutput) == true;
                if (!hasContent)
                {
                    return;
                }

                AddSectionHeading(element, target);
                AddContent(element.Content, target, width);
                break;
            case DocumentElementType.Paragraph:
                AddSubtitle(element.Subtitle, target);
                AddContent(element.Content, target, width);
                break;
            case DocumentElementType.Divider:
                target.Append(Rule());
                break;
            case DocumentElementType.Table:
                AddContent(element.Content, target, width);
                break;
            case DocumentElementType.Chart:
                AddChartOrContent(element, target, width);
                break;
            case DocumentElementType.Document:
            default:
                break;
        }

        foreach (IDocumentElement child in element.Children)
        {
            Process(child, target, width);
        }
    }

    /// <summary>The section's rule and title, its rating, and its subtitle when it has no child blocks to repeat it.</summary>
    private void AddSectionHeading(IDocumentElement element, OpenXmlCompositeElement target)
    {
        if (!string.IsNullOrWhiteSpace(element.Title))
        {
            target.Append(Rule());
            target.Append(StyledParagraph(element.Title, HEADING_HALF_POINTS, bold: true, allCaps: true));
        }

        if (element.Metadata.TryGetValue(RATING_KEY, out string? rating) && !string.IsNullOrWhiteSpace(rating))
        {
            target.Append(StyledParagraph($"{SCORECARD_DRIVER_PREFIX}{rating}", SCORECARD_HALF_POINTS, bold: true));
        }

        if (!element.Children.Any())
        {
            AddSubtitle(element.Subtitle, target);
        }
    }

    private void AddSubtitle(string? subtitle, OpenXmlCompositeElement target)
    {
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            target.Append(StyledParagraph(subtitle, HEADING_HALF_POINTS, bold: true, color: SUBTITLE_COLOR));
        }
    }

    private void AddContent(string? content, OpenXmlCompositeElement target, double width)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        foreach (ContentSegment segment in ContentSegmenter.SegmentContent(content))
        {
            switch (segment.Type)
            {
                case SegmentType.Image:
                    foreach (byte[] image in DataImages.Decode(segment.Content))
                    {
                        target.Append(new Paragraph(_pictures.Run(image, maxWidth: width)));
                    }

                    break;
                case SegmentType.Table:
                    AppendHtml(segment.Content, target, fullWidthTables: true);
                    break;
                case SegmentType.Text:
                default:
                    AppendHtml(FactTags.Strip(segment.Content) ?? segment.Content, target, fullWidthTables: false);
                    break;
            }
        }
    }

    private void AppendHtml(string html, OpenXmlCompositeElement target, bool fullWidthTables)
    {
        foreach (OpenXmlCompositeElement element in _html.Parse(BoldHeaderCells(MarkdownRenderer.WithoutRelativeLinks(html))))
        {
            if (fullWidthTables && element is Table table)
            {
                FitToWidth(table);
            }

            target.Append(element);
        }
    }

    /// <summary>HtmlToOpenXml writes a header cell as plain text, where a browser and the paid export show it in bold.</summary>
    private static string BoldHeaderCells(string html)
    {
        return HeaderCell().Replace(html, "${open}<strong>${content}</strong>${close}");
    }

    [GeneratedRegex(@"(?<open><th\b[^>]*>)(?<content>.*?)(?<close></th>)", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex HeaderCell();

    private void AddChartOrContent(IDocumentElement element, OpenXmlCompositeElement target, double width)
    {
        if (element.OutputData?.OfType<ChartOutput>().FirstOrDefault() is ChartOutput chart)
        {
            AddChart(chart, target, width);
        }
        else
        {
            AddContent(element.Content, target, width);
        }
    }

    /// <summary>The chart as the image the render service draws, or as a table of its data when it draws none.</summary>
    private void AddChart(ChartOutput chart, OpenXmlCompositeElement target, double width)
    {
        double height = chart.ChartConfig.Type?.ToLowerInvariant() is "pie" or "donut" ? width : width * CHART_ASPECT;
        if (ChartPicture(chart, width, height) is byte[] image)
        {
            target.Append(new Paragraph(_pictures.Run(image, width, height)));
            return;
        }

        AddChartTable(chart, target);
    }

    /// <summary>The picture the render service draws of the chart, or null when the build has no service or it fails.</summary>
    private byte[]? ChartPicture(ChartOutput chart, double width, double height)
    {
        if (_charts is null)
        {
            return null;
        }

        try
        {
            return _charts
                .RenderChartToImageAsync(chart.ChartConfig, (int)(width * SCREEN_PIXELS_PER_POINT), (int)(height * SCREEN_PIXELS_PER_POINT), _reportContext)
                .GetAwaiter()
                .GetResult();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Chart image rendering failed; the chart goes in as a table of its data");
            return null;
        }
    }

    private static void AddChartTable(ChartOutput chart, OpenXmlCompositeElement target)
    {
        ChartDataTable data = ChartDataTable.From(chart);
        target.Append(PlainParagraph(data.Caption));

        if (data.Rows.Count == 0)
        {
            target.Append(PlainParagraph(ChartDataTable.NO_DATA_TEXT));
            return;
        }

        var table = new Table(
            new TableProperties(new TableWidth { Type = TableWidthUnitValues.Pct, Width = OpenXmlParts.FULL_WIDTH_PCT }, OpenXmlParts.SingleBorders()),
            new TableGrid(data.Header.Select(_ => new GridColumn())));
        table.Append(Row(data.Header));
        foreach (IReadOnlyList<string> row in data.Rows)
        {
            table.Append(Row(row));
        }

        target.Append(table);
        target.Append(new Paragraph());
    }

    /// <summary>
    /// Each grid row as a borderless table, its blocks in cells as wide as their columns, and a block
    /// that spans the whole row on its own. Blocks without a position follow the grid.
    /// </summary>
    private void AddGrid(IDocumentElement element, GridLayoutOutput grid, OpenXmlCompositeElement target, double width)
    {
        int columns = Math.Clamp(grid.Columns > 0 ? grid.Columns : 2, 1, MAX_GRID_COLUMNS);
        double columnWidth = width / columns;
        List<(GridElementPosition Position, IDocumentElement Child)> positioned = [.. (grid.Elements ?? [])
            .Select(position => (position, child: element.Children.FirstOrDefault(child => child.Order == position.BlockOrder)))
            .Where(item => item.child is not null)
            .Select(item => (item.position, item.child!))];

        foreach (IGrouping<int, (GridElementPosition Position, IDocumentElement Child)> row in positioned
            .GroupBy(item => item.Position.Row)
            .OrderBy(group => group.Key))
        {
            List<(GridElementPosition Position, IDocumentElement Child)> items = [.. row.OrderBy(item => item.Position.Column)];

            if (items.Count == 1 && items[0].Position.Column <= 1 && items[0].Position.ColumnSpan >= columns)
            {
                AddBlock(items[0].Child, target, width);
                continue;
            }

            if (items.Exists(item => item.Child.Type is DocumentElementType.Chart) && items.Exists(item => item.Child.Type is not DocumentElementType.Chart))
            {
                AddRowWithFloatingChart(items, columns, columnWidth, target, width);
                continue;
            }

            var tableRow = new TableRow();
            int cursor = 1;
            foreach ((GridElementPosition position, IDocumentElement child) in items)
            {
                if (position.Column > cursor)
                {
                    tableRow.Append(EmptyGridCell((position.Column - cursor) * columnWidth));
                    cursor = position.Column;
                }

                int span = Math.Max(1, Math.Min(position.ColumnSpan, columns - position.Column + 1));
                TableCell cell = GridCell(span * columnWidth);
                AddBlock(child, cell, span * columnWidth);
                EndWithParagraph(cell);
                tableRow.Append(cell);
                cursor += span;
            }

            if (cursor <= columns)
            {
                tableRow.Append(EmptyGridCell((columns + 1 - cursor) * columnWidth));
            }

            target.Append(new Table(new TableProperties(OpenXmlParts.NoBorders()),
                new TableGrid(tableRow.Elements<TableCell>().Select(cell => new GridColumn { Width = cell.TableCellProperties?.TableCellWidth?.Width })),
                tableRow));
            target.Append(new Paragraph());
        }

        HashSet<int> placed = [.. grid.Elements?.Select(position => position.BlockOrder) ?? []];
        foreach (IDocumentElement child in element.Children.Where(child => !placed.Contains(child.Order)))
        {
            Process(child, target, width);
        }
    }

    /// <summary>
    /// A row that pairs a chart with text, as the paid export lays it out: the chart picture floats at its side of the
    /// page, and the text at full width wraps beside it and reflows across the page below it. A chart that has no
    /// picture goes before the text as a table of its data.
    /// </summary>
    private void AddRowWithFloatingChart(
        List<(GridElementPosition Position, IDocumentElement Child)> items,
        int columns,
        double columnWidth,
        OpenXmlCompositeElement target,
        double width)
    {
        (GridElementPosition chartPosition, IDocumentElement chart) = items.First(item => item.Child.Type is DocumentElementType.Chart);
        List<(GridElementPosition Position, IDocumentElement Child)> text = [.. items.Where(item => item.Child.Type is not DocumentElementType.Chart)];
        double chartWidth = Math.Max(1, Math.Min(chartPosition.ColumnSpan, columns - chartPosition.Column + 1)) * columnWidth;
        double chartHeight = chartWidth * CHART_FLOAT_ASPECT;
        byte[]? picture = chart.OutputData?.OfType<ChartOutput>().FirstOrDefault() is ChartOutput output
            ? ChartPicture(output, chartWidth, chartHeight)
            : null;
        if (picture is null)
        {
            AddBlock(chart, target, width);
        }

        int firstTextElement = target.ChildElements.Count;
        foreach ((GridElementPosition _, IDocumentElement block) in text)
        {
            AddBlock(block, target, width);
        }

        if (picture is not null)
        {
            Run floating = _pictures.FloatingRun(picture, chartWidth, chartHeight, atRight: chartPosition.Column >= text[0].Position.Column);
            Paragraph? first = target.ChildElements.Skip(firstTextElement).OfType<Paragraph>().FirstOrDefault();
            if (first is null)
            {
                target.Append(new Paragraph(floating));
            }
            else
            {
                first.InsertAfter(floating, first.ParagraphProperties);
            }
        }

        target.Append(new Paragraph());
    }

    private void AddBlock(IDocumentElement block, OpenXmlCompositeElement target, double width)
    {
        AddSubtitle(block.Subtitle, target);

        if (block.Type is DocumentElementType.Chart)
        {
            AddChartOrContent(block, target, width);
        }
        else
        {
            AddContent(block.Content, target, width);
        }
    }

    private static TableCell GridCell(double width)
    {
        return new TableCell(new TableCellProperties(new TableCellWidth
        {
            Type = TableWidthUnitValues.Dxa,
            Width = ((int)(width * OpenXmlParts.TWIPS_PER_POINT)).ToString(CultureInfo.InvariantCulture),
        }));
    }

    private static TableCell EmptyGridCell(double width)
    {
        TableCell cell = GridCell(width);
        EndWithParagraph(cell);
        return cell;
    }

    /// <summary>A Word table cell must end with a paragraph.</summary>
    private static void EndWithParagraph(TableCell cell)
    {
        if (cell.LastChild is not Paragraph)
        {
            cell.Append(new Paragraph());
        }
    }

    private void AddReportHeader(IDictionary<string, string?> metadata)
    {
        byte[] banner = ReportBanner.Image;
        _body.Append(new Paragraph(
            new ParagraphProperties
            {
                SpacingBetweenLines = new SpacingBetweenLines { After = BANNER_SPACE_AFTER_TWIPS.ToString(CultureInfo.InvariantCulture) },
                Justification = new Justification { Val = JustificationValues.Center },
            },
            _pictures.Run(banner, maxWidth: _pageWidth, stretchToWidth: true)));
        _body.Append(StyledParagraph(REPORT_TITLE, REPORT_TITLE_HALF_POINTS, bold: true, justification: JustificationValues.Center));
        _body.Append(StyledParagraph(MetadataValue(metadata, DocumentMetadataKeys.COUNTERPARTY), HEADING_HALF_POINTS, bold: true, color: SUBTITLE_COLOR, allCaps: true));
        _body.Append(StyledParagraph(MetadataValue(metadata, DocumentMetadataKeys.ANALYST_NAME), BODY_HALF_POINTS, bold: false));
    }

    private static string MetadataValue(IDictionary<string, string?> metadata, string key)
    {
        return metadata.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : NOT_AVAILABLE;
    }

    private static (uint Width, uint Height, bool Landscape) PageSize(ReportLayout? layout)
    {
        bool letter = layout is null || string.Equals(layout.PageSize, LETTER, StringComparison.OrdinalIgnoreCase);
        bool landscape = string.Equals(layout?.Orientation, LANDSCAPE, StringComparison.OrdinalIgnoreCase);
        (uint shortSide, uint longSide) = letter
            ? (OpenXmlParts.LETTER_SHORT_SIDE_TWIPS, OpenXmlParts.LETTER_LONG_SIDE_TWIPS)
            : (OpenXmlParts.A4_SHORT_SIDE_TWIPS, OpenXmlParts.A4_LONG_SIDE_TWIPS);
        return landscape ? (longSide, shortSide, true) : (shortSide, longSide, false);
    }

    private SectionProperties PageSetup()
    {
        (uint width, uint height, bool landscape) = PageSize(_layout);
        return new SectionProperties(
            new PageSize { Width = width, Height = height, Orient = landscape ? PageOrientationValues.Landscape : PageOrientationValues.Portrait },
            new PageMargin
            {
                Top = MARGIN_TOP_BOTTOM_TWIPS,
                Bottom = MARGIN_TOP_BOTTOM_TWIPS,
                Left = MARGIN_SIDES_TWIPS,
                Right = MARGIN_SIDES_TWIPS,
                Header = HEADER_FOOTER_TWIPS,
                Footer = HEADER_FOOTER_TWIPS,
                Gutter = 0,
            });
    }

    /// <summary>HtmlToOpenXml numbers the pictures it writes apart from ours, so the ids are set once more over the whole body.</summary>
    private void NumberThePictures()
    {
        uint id = 1;
        foreach (DW.DocProperties properties in _body.Descendants<DW.DocProperties>())
        {
            properties.Id = id++;
        }
    }

    /// <summary>A justified line that ends in a line break keeps its spacing, as it does in the paid export.</summary>
    private void AddSettings()
    {
        DocumentSettingsPart settingsPart = _main.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings = new Settings(new Compatibility(new DoNotExpandShiftReturn()));
        settingsPart.Settings.Save();
    }

    private static Paragraph Rule()
    {
        return new Paragraph(new ParagraphProperties(new ParagraphBorders(new BottomBorder
        {
            Val = BorderValues.Single,
            Size = RULE_EIGHTHS_OF_A_POINT,
            Color = RULE_COLOR,
            Space = 1,
        })));
    }

    private static Paragraph StyledParagraph(
        string text,
        int halfPoints,
        bool bold,
        string? color = null,
        bool allCaps = false,
        JustificationValues? justification = null)
    {
        var runProperties = new RunProperties
        {
            Bold = bold ? new Bold() : null,
            Caps = allCaps ? new Caps() : null,
            Color = color is null ? null : new Color { Val = color },
            FontSize = new FontSize { Val = halfPoints.ToString(CultureInfo.InvariantCulture) },
        };

        return new Paragraph(
            new ParagraphProperties
            {
                SpacingBetweenLines = new SpacingBetweenLines { After = SPACE_AFTER_TWIPS.ToString(CultureInfo.InvariantCulture) },
                Justification = new Justification { Val = justification ?? JustificationValues.Left },
            },
            new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Paragraph PlainParagraph(string text)
    {
        return new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static TableRow Row(IEnumerable<string> cells)
    {
        return new TableRow(cells.Select(text => new TableCell(PlainParagraph(text))));
    }

    /// <summary>A table from content spans the page, and keeps the borders its HTML gives it or gets single ones.</summary>
    private static void FitToWidth(Table table)
    {
        TableProperties properties = table.GetFirstChild<TableProperties>() ?? table.PrependChild(new TableProperties());
        properties.TableWidth = new TableWidth { Type = TableWidthUnitValues.Pct, Width = OpenXmlParts.FULL_WIDTH_PCT };
        properties.TableBorders ??= OpenXmlParts.SingleBorders();
    }

    /// <summary>A body paragraph of text no larger than the body size reads justified, as the paid export sets it.</summary>
    private void JustifyBodyParagraphs()
    {
        foreach (Paragraph paragraph in _body.Elements<Paragraph>())
        {
            string? style = paragraph.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
            if (style?.Contains(HEADING_STYLE, StringComparison.OrdinalIgnoreCase) == true)
            {
                continue;
            }

            List<Run> runs = [.. paragraph.Elements<Run>()];
            bool bodySized = runs.Count > 0 && runs.TrueForAll(run =>
                run.RunProperties?.FontSize?.Val?.Value is not string size
                || int.Parse(size, CultureInfo.InvariantCulture) <= BODY_HALF_POINTS);
            bool hasPicture = runs.Exists(run => run.GetFirstChild<Drawing>()?.Inline is not null);

            if (bodySized && !hasPicture && paragraph.ParagraphProperties?.Justification is null)
            {
                ParagraphProperties properties = paragraph.ParagraphProperties ?? paragraph.PrependChild(new ParagraphProperties());
                properties.Justification = new Justification { Val = JustificationValues.Both };
            }
        }
    }

    /// <summary>
    /// A line break in body text starts a paragraph of its own, as the paid export sets it, so each line takes the
    /// paragraph spacing. A list item and a table cell keep their line breaks.
    /// </summary>
    private void SplitParagraphsOnLineBreaks()
    {
        foreach (Paragraph paragraph in _body.Elements<Paragraph>().Where(HasBodyLineBreak).ToList())
        {
            List<List<OpenXmlElement>> lines = [[]];
            foreach (OpenXmlElement child in paragraph.ChildElements.Where(child => child is not ParagraphProperties))
            {
                if (child is not Run run || !run.Elements<Break>().Any(IsLineBreak))
                {
                    lines[^1].Add(child.CloneNode(true));
                    continue;
                }

                Run line = EmptyRunLike(run);
                foreach (OpenXmlElement part in run.ChildElements.Where(part => part is not RunProperties))
                {
                    if (part is Break lineBreak && IsLineBreak(lineBreak))
                    {
                        lines[^1].Add(line);
                        lines.Add([]);
                        line = EmptyRunLike(run);
                    }
                    else
                    {
                        line.Append(part.CloneNode(true));
                    }
                }

                lines[^1].Add(line);
            }

            foreach (List<OpenXmlElement> line in lines.Where(HasContent))
            {
                var split = new Paragraph();
                if (paragraph.ParagraphProperties?.CloneNode(true) is ParagraphProperties properties)
                {
                    split.ParagraphProperties = properties;
                }

                split.Append(line);
                paragraph.InsertBeforeSelf(split);
            }

            paragraph.Remove();
        }
    }

    private static bool HasBodyLineBreak(Paragraph paragraph)
    {
        return paragraph.ParagraphProperties?.NumberingProperties is null
            && paragraph.Elements<Run>().Any(run => run.Elements<Break>().Any(IsLineBreak));
    }

    private static bool IsLineBreak(Break lineBreak)
    {
        return lineBreak.Type is null || lineBreak.Type.Value == BreakValues.TextWrapping;
    }

    private static Run EmptyRunLike(Run run)
    {
        return run.RunProperties?.CloneNode(true) is RunProperties properties ? new Run(properties) : new Run();
    }

    private static bool HasContent(List<OpenXmlElement> line)
    {
        return line.Exists(element => !string.IsNullOrWhiteSpace(element.InnerText) || element.Descendants<Drawing>().Any());
    }

    private void RemoveBlankParagraphsBetweenContent()
    {
        foreach (Paragraph paragraph in _body.Elements<Paragraph>().ToList())
        {
            bool blank = string.IsNullOrWhiteSpace(paragraph.InnerText)
                && !paragraph.Descendants<Drawing>().Any()
                && paragraph.ParagraphProperties?.ParagraphBorders is null;

            if (blank && paragraph.PreviousSibling() is Paragraph or Table && paragraph.NextSibling() is Paragraph)
            {
                paragraph.Remove();
            }
        }
    }

    /// <summary>
    /// A list reads as the paid export sets it: a top-level marker at the margin, each level one step further
    /// in, and the body's spacing after each item. Word takes a list item's indentation from its numbering
    /// level, so the list style keeps none.
    /// </summary>
    private void SetListsLikeTheBody()
    {
        foreach (Level level in _main.NumberingDefinitionsPart?.Numbering?.Descendants<Level>() ?? [])
        {
            int textStart = ((level.LevelIndex?.Value ?? 0) + 1) * LIST_STEP_TWIPS;
            level.PreviousParagraphProperties ??= new PreviousParagraphProperties();
            level.PreviousParagraphProperties.Indentation = new Indentation
            {
                Left = textStart.ToString(CultureInfo.InvariantCulture),
                Hanging = LIST_STEP_TWIPS.ToString(CultureInfo.InvariantCulture),
            };
        }

        foreach (Style style in _main.StyleDefinitionsPart?.Styles?.Elements<Style>() ?? [])
        {
            if (style.StyleId?.Value == LIST_PARAGRAPH_STYLE)
            {
                style.StyleParagraphProperties?.Indentation?.Remove();
                style.StyleParagraphProperties?.SpacingBetweenLines?.Remove();
                style.StyleParagraphProperties?.ContextualSpacing?.Remove();
            }
        }
    }

    /// <summary>Report content never makes the export fetch a URL: only data URIs become images.</summary>
    private sealed class NoDownloads : HtmlToOpenXml.IO.IWebRequest
    {
        public Task<HtmlToOpenXml.IO.Resource?> FetchAsync(Uri requestUri, CancellationToken cancellationToken)
        {
            return Task.FromResult<HtmlToOpenXml.IO.Resource?>(null);
        }

        public bool SupportsProtocol(string protocol)
        {
            return false;
        }
    }
}

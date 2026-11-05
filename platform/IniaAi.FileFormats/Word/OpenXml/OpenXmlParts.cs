using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>The document parts, units and table borders that the open Word writers share.</summary>
internal static class OpenXmlParts
{
    /// <summary>A table width of 100%, in fiftieths of a percent.</summary>
    public const string FULL_WIDTH_PCT = "5000";

    public const int TWIPS_PER_POINT = 20;
    public const uint LETTER_SHORT_SIDE_TWIPS = 12240;
    public const uint LETTER_LONG_SIDE_TWIPS = 15840;
    public const uint A4_SHORT_SIDE_TWIPS = 11906;
    public const uint A4_LONG_SIDE_TWIPS = 16838;

    private const uint SINGLE_BORDER_EIGHTHS_OF_A_POINT = 4;
    private const string NORMAL_STYLE = "Normal";

    /// <summary>The document's default font and size, the space after a paragraph and the line height, in twips.</summary>
    public static void AddDefaultStyle(MainDocumentPart main, string fontName, int halfPoints, int spaceAfterTwips, int lineTwips)
    {
        string size = halfPoints.ToString(CultureInfo.InvariantCulture);
        StyleDefinitionsPart part = main.AddNewPart<StyleDefinitionsPart>();
        part.Styles = new Styles(
            new DocDefaults(
                new RunPropertiesDefault(new RunPropertiesBaseStyle(
                    new RunFonts { Ascii = fontName, HighAnsi = fontName, ComplexScript = fontName, EastAsia = fontName },
                    new FontSize { Val = size },
                    new FontSizeComplexScript { Val = size })),
                new ParagraphPropertiesDefault(new ParagraphPropertiesBaseStyle(new SpacingBetweenLines
                {
                    After = spaceAfterTwips.ToString(CultureInfo.InvariantCulture),
                    Line = lineTwips.ToString(CultureInfo.InvariantCulture),
                    LineRule = LineSpacingRuleValues.Auto,
                }))),
            new Style(new StyleName { Val = NORMAL_STYLE }, new PrimaryStyle()) { Type = StyleValues.Paragraph, StyleId = NORMAL_STYLE, Default = true });
        part.Styles.Save();
    }

    /// <summary>
    /// A "Page X of Y" footer, in the default size unless <paramref name="halfPoints"/> gives one, with
    /// <paramref name="notice"/> on a line above it when given; returns the id a footer reference names.
    /// </summary>
    public static string AddPageOfPagesFooter(MainDocumentPart main, JustificationValues justification, int? halfPoints = null, string? notice = null)
    {
        Run Text(string text)
        {
            var run = new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
            if (halfPoints is int size)
            {
                run.PrependChild(new RunProperties(new FontSize { Val = size.ToString(CultureInfo.InvariantCulture) }));
            }

            return run;
        }

        FooterPart part = main.AddNewPart<FooterPart>();
        part.Footer = new Footer(new Paragraph(
            new ParagraphProperties(new Justification { Val = justification }),
            Text("Page "),
            new SimpleField(Text("1")) { Instruction = " PAGE " },
            Text(" of "),
            new SimpleField(Text("1")) { Instruction = " NUMPAGES " }));
        if (!string.IsNullOrWhiteSpace(notice))
        {
            _ = part.Footer.PrependChild(new Paragraph(new ParagraphProperties(new Justification { Val = justification }), Text(notice)));
        }

        part.Footer.Save();
        return main.GetIdOfPart(part);
    }

    public static TableBorders SingleBorders()
    {
        return Borders(BorderValues.Single, SINGLE_BORDER_EIGHTHS_OF_A_POINT);
    }

    public static TableBorders NoBorders()
    {
        return Borders(BorderValues.None, size: null);
    }

    private static TableBorders Borders(BorderValues value, uint? size)
    {
        return new TableBorders(
            new TopBorder { Val = value, Size = size },
            new LeftBorder { Val = value, Size = size },
            new BottomBorder { Val = value, Size = size },
            new RightBorder { Val = value, Size = size },
            new InsideHorizontalBorder { Val = value, Size = size },
            new InsideVerticalBorder { Val = value, Size = size });
    }
}

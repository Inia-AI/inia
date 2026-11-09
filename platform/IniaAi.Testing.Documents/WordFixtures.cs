using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using IniaAi.FileFormats.Images;
using A = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace IniaAi.Testing.Documents;

/// <summary>
/// Word documents built with Open XML SDK, so that a test of the code around a Word implementation needs no Word
/// implementation of the build under test. The legacy file is committed: Open XML SDK does not write the binary
/// format.
/// </summary>
public static class WordFixtures
{
    /// <summary>A paragraph of this text starts a new page instead.</summary>
    public const string PAGE_BREAK = "\f";

    private const string FOLDER = "TestDocuments";
    private const string LEGACY_DOCUMENT = "legacy.doc";
    private const long EMUS_PER_PIXEL = 9_525;
    private const string PICTURE_URI = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const string NAMESPACES =
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" "
        + "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
        + "xmlns:v=\"urn:schemas-microsoft-com:vml\" "
        + "xmlns:o=\"urn:schemas-microsoft-com:office:office\" "
        + "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" "
        + "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" "
        + "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
        + "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\"";

    /// <summary>A .docx with one paragraph for each text.</summary>
    public static byte[] Document(params string[] paragraphs)
    {
        return Write(_ => Body(paragraphs.Select(text => text == PAGE_BREAK ? PageBreak() : TextParagraph(text))));
    }

    /// <summary>A .docx with the paragraph, then the PNG picture in a paragraph of its own.</summary>
    public static byte[] DocumentWithPicture(string paragraph, byte[] png)
    {
        return Write(main => Body([TextParagraph(paragraph), AddPicture(main, png)()]));
    }

    /// <summary>A .docx with one picture stored once and shown on each of <paramref name="pages"/> pages, as a logo repeated in a body is.</summary>
    public static byte[] DocumentRepeatingOnePicture(byte[] png, int pages)
    {
        return Write(main =>
        {
            Func<Paragraph> picture = AddPicture(main, png);
            List<Paragraph> paragraphs = [];
            for (int page = 1; page <= pages; page++)
            {
                if (page > 1)
                {
                    paragraphs.Add(PageBreak());
                }

                paragraphs.Add(TextParagraph($"Section {page} narrative."));
                paragraphs.Add(picture());
            }

            return Body(paragraphs);
        });
    }

    /// <summary>
    /// A .docx whose body, header, footer, styles and numbering hold the WordprocessingML given, for content the other
    /// builders do not write, such as runs a tag is split across or a text box. The <c>w</c>, <c>r</c>, <c>v</c>,
    /// <c>o</c>, <c>mc</c>, <c>wp</c>, <c>a</c> and <c>wps</c> prefixes are declared.
    /// </summary>
    public static byte[] FromXml(string bodyXml, string? headerXml = null, string? footerXml = null, string? stylesXml = null, string? numberingXml = null)
    {
        return Write(main =>
        {
            if (stylesXml is not null)
            {
                main.AddNewPart<StyleDefinitionsPart>().Styles = new Styles($"<w:styles {NAMESPACES}>{stylesXml}</w:styles>");
            }

            if (numberingXml is not null)
            {
                main.AddNewPart<NumberingDefinitionsPart>().Numbering = new Numbering($"<w:numbering {NAMESPACES}>{numberingXml}</w:numbering>");
            }

            string references = string.Empty;
            if (headerXml is not null)
            {
                HeaderPart header = main.AddNewPart<HeaderPart>();
                header.Header = new Header($"<w:hdr {NAMESPACES}>{headerXml}</w:hdr>");
                references += $"<w:headerReference w:type=\"default\" r:id=\"{main.GetIdOfPart(header)}\"/>";
            }

            if (footerXml is not null)
            {
                FooterPart footer = main.AddNewPart<FooterPart>();
                footer.Footer = new Footer($"<w:ftr {NAMESPACES}>{footerXml}</w:ftr>");
                references += $"<w:footerReference w:type=\"default\" r:id=\"{main.GetIdOfPart(footer)}\"/>";
            }

            return new Document($"<w:document {NAMESPACES}><w:body>{bodyXml}<w:sectPr>{references}</w:sectPr></w:body></w:document>");
        });
    }

    /// <summary>A Word 97-2003 .doc file.</summary>
    public static byte[] Legacy()
    {
        return File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, FOLDER, LEGACY_DOCUMENT));
    }

    private static byte[] Write(Func<MainDocumentPart, Document> content)
    {
        using var stream = new MemoryStream();
        using (WordprocessingDocument document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            MainDocumentPart main = document.AddMainDocumentPart();
            main.Document = content(main);
        }

        return stream.ToArray();
    }

    /// <summary>Stores the PNG in the document once, and gives a new paragraph that shows it each time it is called.</summary>
    private static Func<Paragraph> AddPicture(MainDocumentPart main, byte[] png)
    {
        ImagePart image = main.AddImagePart(ImagePartType.Png);
        using (var source = new MemoryStream(png))
        {
            image.FeedData(source);
        }

        PictureSize size = Pictures.ReadSize(png) ?? throw new ArgumentException("The bytes hold no PNG picture.", nameof(png));
        string relationshipId = main.GetIdOfPart(image);
        return () => new Paragraph(new Run(Picture(relationshipId, size)));
    }

    private static Paragraph PageBreak()
    {
        return new Paragraph(new Run(new Break { Type = BreakValues.Page }));
    }

    private static Document Body(IEnumerable<Paragraph> paragraphs)
    {
        return new Document(new Body(paragraphs));
    }

    private static Paragraph TextParagraph(string text)
    {
        return new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }

    private static Drawing Picture(string relationshipId, PictureSize size)
    {
        long width = size.Width * EMUS_PER_PIXEL;
        long height = size.Height * EMUS_PER_PIXEL;
        return new Drawing(new Wp.Inline(
            new Wp.Extent { Cx = width, Cy = height },
            new Wp.DocProperties { Id = 1U, Name = "Picture 1" },
            new A.Graphic(new A.GraphicData(
                new Pic.Picture(
                    new Pic.NonVisualPictureProperties(new Pic.NonVisualDrawingProperties { Id = 0U, Name = "picture.png" }, new Pic.NonVisualPictureDrawingProperties()),
                    new Pic.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
                    new Pic.ShapeProperties(
                        new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = width, Cy = height }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = PICTURE_URI })));
    }
}

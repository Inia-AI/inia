using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace IniaAi.FileFormats.Word.OpenXml;

internal sealed class OpenXmlWordTemplate : IWordTemplate
{
    private const string BLOCK_SEPARATOR = "\n\n";

    private readonly WordprocessingDocument _document;
    private readonly MemoryStream _stream;
    private readonly MainDocumentPart _main;
    private readonly HashSet<Paragraph> _valueParagraphs = [];

    public OpenXmlWordTemplate(WordprocessingDocument document, MemoryStream stream)
    {
        _document = document;
        _stream = stream;
        _main = document.MainDocumentPart ?? throw new InvalidDataException("The package holds no Word document.");
        Paragraphs = [.. AllParagraphs()
            .Where(paragraph => !paragraph.Ancestors<AlternateContentFallback>().Any())
            .Select(paragraph => new OpenXmlWordTemplateParagraph(paragraph, _main, _valueParagraphs))];
    }

    public IReadOnlyList<IWordTemplateParagraph> Paragraphs { get; }

    // Word writes a text box twice, once for older readers, so the older copy is filled too but not listed.
    public void ReplaceText(Regex pattern, Func<Match, string> replacement)
    {
        foreach (Paragraph paragraph in AllParagraphs().Where(paragraph => !_valueParagraphs.Contains(paragraph)).ToList())
        {
            ParagraphRuns.Replace(paragraph, pattern, replacement);
        }
    }

    public string? ToMarkdown()
    {
        try
        {
            List<TextBlock> blocks = new OpenXmlBlockWalker(_main).Walk().Blocks;
            string markdown = string.Join(BLOCK_SEPARATOR, blocks.Select(block => block.Markdown));
            return string.IsNullOrWhiteSpace(markdown) ? null : markdown;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // A best-effort view for later blocks; the document itself is the deliverable.
            return null;
        }
    }

    public byte[] Save()
    {
        _document.Save();
        return _stream.ToArray();
    }

    public void Dispose()
    {
        _document.Dispose();
        _stream.Dispose();
    }

    private IEnumerable<Paragraph> AllParagraphs()
    {
        OpenXmlElement?[] roots =
        [
            _main.Document,
            .. _main.HeaderParts.Select(part => part.Header),
            .. _main.FooterParts.Select(part => part.Footer),
            _main.FootnotesPart?.Footnotes,
            _main.EndnotesPart?.Endnotes,
        ];
        return roots.OfType<OpenXmlElement>().SelectMany(root => root.Descendants<Paragraph>());
    }
}

using DocumentFormat.OpenXml.Packaging;
using IniaAi.Common.Content;
using IniaAi.Configuration;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Unlock;
using Microsoft.Extensions.Options;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>
/// The open build's Word reader, on Open XML SDK, for .docx and .docm files. It has no layout engine, so a page is
/// <c>IniaAi:WordCountPerPage</c> words, not a page as Word draws it. Tracked changes show accepted, a field shows its
/// result, a text box follows its paragraph, and a section's header and footer lines close the page that holds the
/// section's first block. A Windows metafile picture is left out: no free image library decodes one.
/// </summary>
public sealed class OpenXmlWordPageReader(IOptions<PlatformSettings> settings) : IWordPageReader
{
    private readonly int _wordsPerPage = settings.Value.IniaAi.WordCountPerPage;

    public IReadOnlyList<WordPageContent> ExtractPages(byte[] documentBytes, string documentName, VisionTier visionTier)
    {
        return Read(documentBytes, documentName, int.MaxValue, visionTier);
    }

    public IReadOnlyList<string> ExtractPageTexts(byte[] documentBytes, string documentName, int maxPages)
    {
        return [.. Read(documentBytes, documentName, maxPages, visionTier: null).Select(page => page.Text)];
    }

    private List<WordPageContent> Read(byte[] documentBytes, string documentName, int maxPages, VisionTier? visionTier)
    {
        if (EncryptedContainerProbe.Detect(documentBytes) is EncryptedContainer.OfficePackage)
        {
            throw new DocumentPasswordProtectedException(documentName);
        }

        using var stream = new MemoryStream(documentBytes, writable: false);
        using WordprocessingDocument document = WordprocessingDocument.Open(stream, isEditable: false);
        MainDocumentPart main = document.MainDocumentPart ?? throw new InvalidDataException("The package holds no Word document.");
        (List<TextBlock> blocks, Dictionary<int, IReadOnlyList<string>> headerFooterLines) = new OpenXmlBlockWalker(main).Walk();
        return WordCountPages.Split(blocks, headerFooterLines, _wordsPerPage, maxPages, visionTier);
    }
}

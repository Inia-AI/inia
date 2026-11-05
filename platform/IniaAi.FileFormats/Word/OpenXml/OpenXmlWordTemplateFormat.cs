using DocumentFormat.OpenXml.Packaging;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>The open build's Word templates, on Open XML SDK, for .docx and .docm documents.</summary>
public sealed class OpenXmlWordTemplateFormat : IWordTemplateFormat
{
    private const string DOCX = ".docx";
    private const string DOCM = ".docm";
    private const string DOCUMENT_CONTENT_TYPE = "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml";
    private const string MACRO_DOCUMENT_CONTENT_TYPE = "application/vnd.ms-word.document.macroEnabled.main+xml";

    public IWordTemplate Open(byte[] templateBytes)
    {
        var stream = new MemoryStream();
        try
        {
            stream.Write(templateBytes);
            return new OpenXmlWordTemplate(WordprocessingDocument.Open(stream, isEditable: true), stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public string? SaveExtension(byte[] documentBytes)
    {
        try
        {
            using var stream = new MemoryStream(documentBytes, writable: false);
            using WordprocessingDocument document = WordprocessingDocument.Open(stream, isEditable: false);
            return document.MainDocumentPart?.ContentType switch
            {
                DOCUMENT_CONTENT_TYPE => DOCX,
                MACRO_DOCUMENT_CONTENT_TYPE => DOCM,
                _ => null,
            };
        }
        catch (Exception e) when (OpenXmlPackageFailure.IsUnreadablePackage(e))
        {
            return null;
        }
    }
}

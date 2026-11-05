using IniaAi.Common;
using IniaAi.WebApi.Contracts.Enums;

using MediaType = IniaAi.Common.Enums.MediaType;

namespace IniaAi.Application.Documents;

public static class TextDocuments
{
    private const string PLAIN_TEXT_EXTENSION = ".txt";
    private const string MARKDOWN_EXTENSION = ".md";

    public static MediaType MediaTypeOf(TextDocumentFormat format)
    {
        return format is TextDocumentFormat.Markdown ? MediaType.TextMarkdown : MediaType.TextPlain;
    }

    public static string FileNameOf(string name, TextDocumentFormat format)
    {
        return name.Trim().WithExtension(format is TextDocumentFormat.Markdown ? MARKDOWN_EXTENSION : PLAIN_TEXT_EXTENSION);
    }
}

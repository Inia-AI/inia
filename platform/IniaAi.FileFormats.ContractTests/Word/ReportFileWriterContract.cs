using IniaAi.Common.Content;
using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;
using IniaAi.Domain.Enums;
using IniaAi.FileFormats.Word;
using IniaAi.Testing.Ooxml;
using System.Net.Http.Headers;
using System.Text;

namespace IniaAi.FileFormats.ContractTests.Word;

/// <summary>
/// What the report export relies on from every <see cref="IReportFileWriter"/>: the extension and
/// content type of the format it names, which the export hands to the download, and a file of that
/// format that carries the report.
/// </summary>
public abstract class ReportFileWriterContract
{
    private const string REPORT_TITLE = "Quarterly credit review";
    private const string SECTION_TITLE = "Summary";
    private const string PARAGRAPH = "Leverage stays inside the covenant.";
    private const string PARAGRAPH_HTML = "<p>" + PARAGRAPH + "</p>";
    private const string UTF8_CHARSET = "utf-8";

    private static readonly Dictionary<ExportFormat, (string Extension, MediaType MediaType)> _formats = new()
    {
        [ExportFormat.Word] = (".docx", MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument),
        [ExportFormat.Markdown] = (".md", MediaType.TextMarkdown),
    };

    protected abstract IReportFileWriter Writer { get; }

    [Fact]
    public void FormatExtensionAndContentType_AreThoseOfTheFormatItWrites()
    {
        IReportFileWriter writer = Writer;

        Assert.True(_formats.TryGetValue(writer.Format, out (string Extension, MediaType MediaType) expected), $"The contract does not know the {writer.Format} format.");
        (string extension, MediaType mediaType) = expected;
        Assert.Equal(extension, writer.Extension);
        Assert.True(MediaTypeHeaderValue.TryParse(writer.ContentType, out MediaTypeHeaderValue? contentType), $"\"{writer.ContentType}\" is not a content type.");
        Assert.Equal(mediaType.GetEnumMember(), contentType.MediaType);
        Assert.True(contentType.CharSet is null || string.Equals(contentType.CharSet, UTF8_CHARSET, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WriteAsync_OfASmallReport_WritesAFileOfItsFormatThatCarriesTheReport()
    {
        IReportFileWriter writer = Writer;

        BinaryContent file = await writer.WriteAsync(SmallReport(), new ReportFileOptions());

        Assert.Equal(_formats[writer.Format].MediaType, file.MediaType);
        byte[] bytes = file.BinaryData.ToArray();
        Assert.NotEmpty(bytes);
        Assert.Contains(PARAGRAPH, TextOf(writer.Format, bytes));
    }

    private static DocumentElement SmallReport()
    {
        return new DocumentElement
        {
            Type = DocumentElementType.Document,
            Title = REPORT_TITLE,
            Children =
            [
                new DocumentElement
                {
                    Type = DocumentElementType.Section,
                    Title = SECTION_TITLE,
                    Children = [new DocumentElement { Type = DocumentElementType.Paragraph, Content = PARAGRAPH_HTML }],
                },
            ],
        };
    }

    private static string TextOf(ExportFormat format, byte[] file)
    {
        return format switch
        {
            ExportFormat.Word => new WordFile(file).AllText,
            ExportFormat.Markdown => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(file),
            _ => throw new NotSupportedException($"The contract cannot read a {format} file yet."),
        };
    }
}

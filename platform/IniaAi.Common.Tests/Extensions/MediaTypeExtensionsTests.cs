using IniaAi.Common.Enums;
using IniaAi.Common.Extensions;

namespace IniaAi.Common.Tests.Extensions;

public class MediaTypeExtensionsTests
{
    [Theory]
    [InlineData(MediaType.ApplicationPdf)]
    [InlineData(MediaType.ImageJpeg)]
    [InlineData(MediaType.ImagePng)]
    [InlineData(MediaType.TextCsv)]
    [InlineData(MediaType.ApplicationVndMsExcel)]
    [InlineData(MediaType.ApplicationVndOpenXmlFormatsOfficeDocumentSpreadsheetMlSheet)]
    [InlineData(MediaType.ApplicationVndMsExcelSheetMacroEnabled12)]
    [InlineData(MediaType.ApplicationVndOpenXmlFormatsOfficedocumentWordprocessingmlDocument)]
    [InlineData(MediaType.TextPlain)]
    [InlineData(MediaType.TextMarkdown)]
    public void ProducesPages_ForEveryFormatTheProcessingDispatchHandles(MediaType mediaType)
    {
        Assert.True(mediaType.ProducesPages());
    }

    [Theory]
    [InlineData(MediaType.MessageRfc822)]
    [InlineData(MediaType.ApplicationVndMsOutlook)]
    [InlineData(MediaType.ApplicationMsWord)]
    [InlineData(MediaType.ApplicationJson)]
    [InlineData(MediaType.Unknown)]
    public void ProducesPages_FalseForEmailLegacyWordAndAnythingUnhandled(MediaType mediaType)
    {
        Assert.False(mediaType.ProducesPages());
    }

    [Theory]
    [InlineData(".md", MediaType.TextMarkdown)]
    [InlineData(".MD", MediaType.TextMarkdown)]
    [InlineData(".markdown", MediaType.TextMarkdown)]
    [InlineData(".txt", MediaType.TextPlain)]
    public void TextExtensions_MapToTheirMediaTypes(string extension, MediaType expected)
    {
        Assert.Equal(expected, Utils.DetermineMediaTypeFromExtension(extension));
    }

    [Fact]
    public void TextMarkdown_CarriesItsMimeType()
    {
        Assert.Equal("text/markdown", MediaType.TextMarkdown.GetEnumMember());
    }
}

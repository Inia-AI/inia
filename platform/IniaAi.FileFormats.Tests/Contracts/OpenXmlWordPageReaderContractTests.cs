using IniaAi.Common.Content;
using IniaAi.Configuration;
using IniaAi.FileFormats.ContractTests;
using IniaAi.FileFormats.ContractTests.Word;
using IniaAi.FileFormats.Word;
using IniaAi.FileFormats.Word.OpenXml;
using IniaAi.Testing.Documents;
using Microsoft.Extensions.Options;
using System.IO.Compression;

namespace IniaAi.FileFormats.Tests.Contracts;

public class OpenXmlWordPageReaderContractTests : WordPageReaderContract
{
    protected override IWordPageReader Reader { get; } = new OpenXmlWordPageReader(Options.Create(new PlatformSettings()));

    [Fact]
    public void ExtractPages_ReadsAPictureTheDocumentRepeats_Once()
    {
        byte[] document = WordFixtures.DocumentRepeatingOnePicture(PictureOf(ContractFixture.Bytes(ContractFixture.PICTURE_WORD_DOCUMENT)), pages: 3);

        List<BinaryContent> regions = [.. Reader.ExtractPages(document, "letter.docx", VisionTier.Standard).SelectMany(page => page.ImageRegions)];

        Assert.Equal(3, regions.Count);
        Assert.All(regions, region => Assert.Same(regions[0], region));
    }

    private static byte[] PictureOf(byte[] document)
    {
        using var package = new ZipArchive(new MemoryStream(document), ZipArchiveMode.Read);
        using Stream picture = package.Entries.First(entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal)).Open();
        using var bytes = new MemoryStream();
        picture.CopyTo(bytes);
        return bytes.ToArray();
    }
}

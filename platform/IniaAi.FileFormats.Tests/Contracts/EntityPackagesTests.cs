using System.IO.Compression;
using System.Xml;
using IniaAi.FileFormats.ContractTests;

namespace IniaAi.FileFormats.Tests.Contracts;

/// <summary>
/// The entity packages catch a reader that processes a document type declaration, so a contract case that finds
/// nothing means the reader under test does not process it.
/// </summary>
public class EntityPackagesTests
{
    private const string DOCUMENT_PART = "word/document.xml";

    private static readonly XmlReaderSettings _processingReader = new()
    {
        DtdProcessing = DtdProcessing.Parse,
        XmlResolver = new XmlUrlResolver(),
    };

    [Fact]
    public void AReaderThatProcessesTheDeclaration_FetchesTheExternalDtd()
    {
        using var probe = new LoopbackProbe();
        byte[] package = EntityPackages.NamingAnExternalDtd(ContractFixture.Bytes(ContractFixture.TEN_PAGES_WORD_DOCUMENT), DOCUMENT_PART, probe.Address);

        _ = Assert.ThrowsAny<Exception>(() => TextOf(package));

        Assert.NotEqual(0, probe.Connections);
    }

    [Fact]
    public async Task AReaderThatProcessesTheDeclaration_ReadsInTheFile()
    {
        string marker = $"marker-{Guid.NewGuid():N}";
        string file = Path.Combine(Path.GetTempPath(), $"entity-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(file, marker, TestContext.Current.CancellationToken);

        try
        {
            Assert.Contains(marker, TextOf(EntityPackages.ReadingInAFile(ContractFixture.Bytes(ContractFixture.TEN_PAGES_WORD_DOCUMENT), DOCUMENT_PART, file)));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void AReaderThatProcessesTheDeclaration_ExpandsTheEntitiesNineTimes()
    {
        string text = TextOf(EntityPackages.WithNestedEntities(ContractFixture.Bytes(ContractFixture.TEN_PAGES_WORD_DOCUMENT), DOCUMENT_PART));

        Assert.Contains(string.Concat(Enumerable.Repeat(EntityPackages.ENTITY_TEXT, 9)), text);
    }

    private static string TextOf(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        using XmlReader reader = XmlReader.Create(zip.GetEntry(DOCUMENT_PART)!.Open(), _processingReader);
        var text = new System.Text.StringBuilder();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Text)
            {
                _ = text.Append(reader.Value);
            }
        }

        return text.ToString();
    }
}

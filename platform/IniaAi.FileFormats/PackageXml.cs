using System.Xml;
using System.Xml.Linq;

namespace IniaAi.FileFormats;

/// <summary>
/// Loads an XML part of a file that someone uploaded. A part that opens with a document type declaration fails with
/// <see cref="XmlException"/>, so no entity is fetched, read in from a local file or expanded.
/// </summary>
internal static class PackageXml
{
    private static readonly XmlReaderSettings _settings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    public static XDocument Load(Stream stream)
    {
        using XmlReader reader = XmlReader.Create(stream, _settings);
        return XDocument.Load(reader);
    }

    public static XDocument Parse(string text)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(text), _settings);
        return XDocument.Load(reader);
    }
}

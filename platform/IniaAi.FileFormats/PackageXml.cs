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
        using XmlReader reader = Reader(stream);
        return XDocument.Load(reader);
    }

    /// <summary>Reads a part forward only, for a caller that needs its start and not the whole part in memory.</summary>
    public static XmlReader Reader(Stream stream)
    {
        return XmlReader.Create(stream, _settings);
    }

    public static XDocument Parse(string text)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(text), _settings);
        return XDocument.Load(reader);
    }
}

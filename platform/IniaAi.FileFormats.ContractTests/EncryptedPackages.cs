using System.Text;
using OpenMcdf;

namespace IniaAi.FileFormats.ContractTests;

/// <summary>An encrypted Office file rebuilt with one of its two streams changed, as a stranger can write it.</summary>
public static class EncryptedPackages
{
    public const string ENCRYPTION_INFO = "EncryptionInfo";
    public const string ENCRYPTED_PACKAGE = "EncryptedPackage";

    // An agile encryption description is XML after an eight-byte version header.
    private const int AGILE_XML_OFFSET = 8;

    /// <summary><paramref name="encrypted"/> with the XML of its agile encryption description changed.</summary>
    public static byte[] WithEncryptionDescription(byte[] encrypted, Func<string, string> change)
    {
        return Rebuilt(encrypted, ENCRYPTION_INFO, info =>
        {
            string xml = Encoding.UTF8.GetString(info, AGILE_XML_OFFSET, info.Length - AGILE_XML_OFFSET);
            return [.. info.AsSpan(0, AGILE_XML_OFFSET), .. Encoding.UTF8.GetBytes(change(xml))];
        });
    }

    /// <summary>
    /// The encryption description's XML with a document type declaration after its XML declaration.
    /// </summary>
    public static string WithDocumentType(string xml, string internalSubset)
    {
        int declarationEnd = xml.IndexOf("?>", StringComparison.Ordinal) + 2;
        return $"{xml[..declarationEnd]}<!DOCTYPE encryption [{internalSubset}]>{xml[declarationEnd..]}";
    }

    public static byte[] Rebuilt(byte[] encrypted, string changedStream, Func<byte[], byte[]> change)
    {
        Dictionary<string, byte[]> streams = [];
        using (var input = new MemoryStream(encrypted, writable: false))
        using (RootStorage source = RootStorage.Open(input, StorageModeFlags.LeaveOpen))
        {
            foreach (string name in new[] { ENCRYPTION_INFO, ENCRYPTED_PACKAGE })
            {
                using CfbStream stream = source.OpenStream(name);
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                streams[name] = name == changedStream ? change(bytes.ToArray()) : bytes.ToArray();
            }
        }

        using var output = new MemoryStream();
        using (RootStorage target = RootStorage.Create(output, OpenMcdf.Version.V3, StorageModeFlags.LeaveOpen))
        {
            foreach ((string name, byte[] bytes) in streams)
            {
                using CfbStream stream = target.CreateStream(name);
                stream.Write(bytes);
            }
        }

        return output.ToArray();
    }
}

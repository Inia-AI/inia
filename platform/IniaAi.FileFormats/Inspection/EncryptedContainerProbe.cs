using IniaAi.FileFormats.Inspection.Checks;
using IniaAi.FileFormats.Pdf;

namespace IniaAi.FileFormats.Inspection;

/// <summary>
/// Names the encrypted container a file is, from its bytes alone. An encrypted PDF declares
/// <c>/Encrypt</c> in its trailer; an encrypted Office package is a compound file wrapping an
/// <c>EncryptedPackage</c> stream; an encrypted legacy workbook carries a <c>FILEPASS</c> record
/// at the head of its <c>Workbook</c> stream. Null means the bytes carry no open-password
/// encryption the platform unlocks, and nothing here costs more than one read of the compound
/// file's directory. A named container can still be one no password opens: an unlocker answers
/// a rights-managed package (<see cref="IsRightsManagedPackage"/>) with unsupported protection.
/// </summary>
public static class EncryptedContainerProbe
{
    private static readonly byte[] _pdfEncryptKey = "/Encrypt"u8.ToArray();

    public static EncryptedContainer? Detect(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.AsSpan().StartsWith(PdfUtils.Signature))
        {
            return PdfDeclaresEncryption(bytes) ? EncryptedContainer.Pdf : null;
        }

        if (!CompoundFileProbe.IsCompoundFile(bytes))
        {
            return null;
        }

        if (CompoundFileProbe.IsEncryptedPackage(bytes))
        {
            return EncryptedContainer.OfficePackage;
        }

        return CompoundFileProbe.WorkbookHasPasswordRecord(bytes) ? EncryptedContainer.LegacyWorkbook : null;
    }

    /// <summary>
    /// True for an encrypted Office package whose key comes from a rights-management licence
    /// ([MS-OFFCRYPTO] 2.2.10): it has an <c>EncryptedPackage</c> stream but no
    /// <c>EncryptionInfo</c> stream, so no password opens it.
    /// </summary>
    public static bool IsRightsManagedPackage(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return CompoundFileProbe.IsCompoundFile(bytes) && CompoundFileProbe.IsRightsManagedPackage(bytes);
    }

    private static bool PdfDeclaresEncryption(byte[] bytes)
    {
        ReadOnlySpan<byte> span = bytes;
        int offset = 0;
        while (true)
        {
            int index = span[offset..].IndexOf(_pdfEncryptKey);
            if (index < 0)
            {
                return false;
            }

            int next = offset + index + _pdfEncryptKey.Length;
            if (next >= span.Length || !IsPdfRegularCharacter(span[next]))
            {
                return true;
            }

            offset = next;
        }
    }

    private static bool IsPdfRegularCharacter(byte b)
    {
        return b is not ((byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or (byte)'\f' or 0
            or (byte)'/' or (byte)'[' or (byte)']' or (byte)'<' or (byte)'>' or (byte)'(' or (byte)')' or (byte)'{' or (byte)'}' or (byte)'%');
    }
}

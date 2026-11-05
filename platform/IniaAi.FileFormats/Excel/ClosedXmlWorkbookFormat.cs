using System.Runtime.ExceptionServices;
using ClosedXML.Excel;
using IniaAi.FileFormats.Inspection;
using IniaAi.FileFormats.Inspection.Checks;
using IniaAi.FileFormats.Unlock;

namespace IniaAi.FileFormats.Excel;

/// <summary>
/// The open build's workbook reader, on ClosedXML, for .xlsx and .xlsm files; the legacy .xls format is
/// paid-only. A chart sheet is not listed: ClosedXML does not load one.
/// </summary>
public sealed class ClosedXmlWorkbookFormat : IWorkbookFormat
{
    private static readonly byte[] _zipSignature = "PK"u8.ToArray();

    public IWorkbookDocument Open(Stream stream, string documentName)
    {
        try
        {
            if (CompoundFile(stream) is byte[] compoundFile)
            {
                throw CompoundFileProbe.IsEncryptedPackage(compoundFile)
                    ? new DocumentPasswordProtectedException(documentName)
                    : new InvalidDataException(OpenBuildFormats.LEGACY_WORKBOOK_NOT_READ);
            }

            return new ClosedXmlWorkbookDocument(LoadForReading(stream), stream);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// ClosedXML fails to load some comments, threaded-comment people and pictures, and a package that names a people
    /// part it lacks. Reading cells needs none of them, so such a workbook loads again from a copy without them; when
    /// that copy fails too, the first failure is the one that explains the file.
    /// </summary>
    internal static XLWorkbook LoadForReading(Stream stream)
    {
        long start = stream.CanSeek ? stream.Position : 0;
        try
        {
            return new XLWorkbook(stream);
        }
        catch (Exception original) when (original is not OutOfMemoryException && StartsWith(stream, start, _zipSignature))
        {
            stream.Position = start;
            try
            {
                using MemoryStream stripped = WorkbookPackage.WithoutCommentsAndDrawings(stream);
                return new XLWorkbook(stripped);
            }
            catch (Exception retry) when (retry is not OutOfMemoryException)
            {
                ExceptionDispatchInfo.Throw(original);
                throw;
            }
        }
    }

    /// <summary>
    /// The bytes of a compound file, which no Office Open XML reader opens: an encrypted package that still needs its
    /// password, or a legacy workbook. Null for anything else.
    /// </summary>
    private static byte[]? CompoundFile(Stream stream)
    {
        long start = stream.CanSeek ? stream.Position : 0;
        if (!StartsWith(stream, start, CompoundFileProbe.Signature))
        {
            return null;
        }

        byte[] bytes = new byte[stream.Length - start];
        stream.ReadExactly(bytes);
        stream.Position = start;
        return bytes;
    }

    private static bool StartsWith(Stream stream, long start, ReadOnlySpan<byte> signature)
    {
        if (!stream.CanSeek)
        {
            return false;
        }

        Span<byte> head = stackalloc byte[signature.Length];
        stream.Position = start;
        int read = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        stream.Position = start;
        return read == head.Length && head.SequenceEqual(signature);
    }
}

internal sealed class ClosedXmlWorkbookDocument(XLWorkbook workbook, Stream source) : IWorkbookDocument
{
    public IReadOnlyList<IWorkbookSheet> Sheets { get; } =
        [.. workbook.Worksheets.OrderBy(sheet => sheet.Position).Select(sheet => new ClosedXmlSheet(sheet))];

    public void Dispose()
    {
        workbook.Dispose();
        source.Dispose();
    }
}

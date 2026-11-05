using System.Runtime.InteropServices;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>A PDF file loaded into PDFium from unmanaged memory, which PDFium reads for as long as the document is open.</summary>
internal sealed unsafe class PdfiumFile : IDisposable
{
    private readonly IntPtr _bytes;
    private bool _disposed;

    private PdfiumFile(IntPtr bytes, IntPtr document)
    {
        _bytes = bytes;
        Document = document;
        PageCount = PdfiumNative.FPDF_GetPageCount(document);
    }

    public IntPtr Document { get; }

    public int PageCount { get; }

    /// <summary>True when the file carries a security handler, even one with no open password.</summary>
    public bool IsEncrypted => PdfiumNative.FPDF_GetSecurityHandlerRevision(Document) != PdfiumNative.NOT_ENCRYPTED;

    public int SignatureCount => PdfiumNative.FPDF_GetSignatureCount(Document);

    /// <summary>
    /// Loads the next <paramref name="length"/> bytes of <paramref name="source"/>. Throws <see cref="PdfiumLoadException"/>
    /// with PDFium's error code when the file does not open.
    /// </summary>
    public static PdfiumFile Load(Stream source, int length)
    {
        IntPtr bytes = Marshal.AllocHGlobal(Math.Max(length, 1));
        try
        {
            source.ReadExactly(new Span<byte>((void*)bytes, length));
            return Open(bytes, length, password: null);
        }
        catch
        {
            Marshal.FreeHGlobal(bytes);
            throw;
        }
    }

    /// <inheritdoc cref="Load(Stream, int)"/>
    public static PdfiumFile Load(ReadOnlySpan<byte> file, string? password)
    {
        IntPtr bytes = Marshal.AllocHGlobal(Math.Max(file.Length, 1));
        try
        {
            file.CopyTo(new Span<byte>((void*)bytes, file.Length));
            return Open(bytes, file.Length, password);
        }
        catch
        {
            Marshal.FreeHGlobal(bytes);
            throw;
        }
    }

    /// <summary>A full copy of the file with its security handler removed.</summary>
    public byte[] SaveWithoutSecurity()
    {
        using var output = new MemoryStream();
        GCHandle target = GCHandle.Alloc(output);
        try
        {
            var writer = new TargetedFileWrite
            {
                Write = new PdfiumNative.FileWrite { Version = 1, WriteBlock = &WriteBlock },
                Target = GCHandle.ToIntPtr(target),
            };

            if (PdfiumNative.FPDF_SaveAsCopy(Document, &writer.Write, new CULong((uint)(PdfiumNative.FPDF_NO_INCREMENTAL | PdfiumNative.FPDF_REMOVE_SECURITY))) == 0)
            {
                throw new InvalidOperationException("PDFium could not save the file without its security.");
            }
        }
        finally
        {
            target.Free();
        }

        return output.ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        PdfiumNative.FPDF_CloseDocument(Document);
        Marshal.FreeHGlobal(_bytes);
    }

    private static PdfiumFile Open(IntPtr bytes, int length, string? password)
    {
        IntPtr document = PdfiumNative.FPDF_LoadMemDocument64((void*)bytes, (nuint)length, password);
        return document == IntPtr.Zero
            ? throw new PdfiumLoadException((int)PdfiumNative.FPDF_GetLastError().Value)
            : new PdfiumFile(bytes, document);
    }

    /// <summary>PDFium hands the write structure back to <see cref="WriteBlock"/>, so the target stream rides behind it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TargetedFileWrite
    {
        public PdfiumNative.FileWrite Write;
        public IntPtr Target;
    }

    [UnmanagedCallersOnly]
    private static int WriteBlock(PdfiumNative.FileWrite* self, void* data, CULong size)
    {
        var stream = (Stream)GCHandle.FromIntPtr(((TargetedFileWrite*)self)->Target).Target!;
        stream.Write(new ReadOnlySpan<byte>(data, checked((int)size.Value)));
        return 1;
    }
}

internal sealed class PdfiumLoadException(int errorCode)
    : Exception($"PDFium could not open the file (error {errorCode}).")
{
    public int ErrorCode { get; } = errorCode;
}

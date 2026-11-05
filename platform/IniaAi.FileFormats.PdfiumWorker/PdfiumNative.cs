using System.Runtime.InteropServices;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>
/// The PDFium functions the worker calls, from the bblanchon.PDFium native packages. PDFium is not thread-safe, and
/// the worker calls it from one thread. A C <c>unsigned long</c> is 32 bits on Windows and 64 bits elsewhere, hence
/// <see cref="CULong"/>.
/// </summary>
internal static unsafe partial class PdfiumNative
{
    public const int FPDF_ERR_FORMAT = 3;
    public const int FPDF_ERR_PASSWORD = 4;
    public const int FPDF_ERR_SECURITY = 5;
    public const int FPDF_ANNOT = 0x01;
    public const int FPDF_NO_INCREMENTAL = 1 << 1;
    public const int FPDF_REMOVE_SECURITY = 1 << 2;
    public const int NOT_ENCRYPTED = -1;

    private const string LIBRARY = "pdfium";

    [StructLayout(LayoutKind.Sequential)]
    public struct FsMatrix
    {
        public float A;
        public float B;
        public float C;
        public float D;
        public float E;
        public float F;
    }

    /// <summary>Page coordinates, so <see cref="Top"/> is above <see cref="Bottom"/>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct FsRectF
    {
        public float Left;
        public float Top;
        public float Right;
        public float Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FileWrite
    {
        public int Version;
        public delegate* unmanaged<FileWrite*, void*, CULong, int> WriteBlock;
    }

    [LibraryImport(LIBRARY)]
    public static partial void FPDF_InitLibrary();

    [LibraryImport(LIBRARY, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr FPDF_LoadMemDocument64(void* data, nuint size, string? password);

    [LibraryImport(LIBRARY)]
    public static partial void FPDF_CloseDocument(IntPtr document);

    [LibraryImport(LIBRARY)]
    public static partial CULong FPDF_GetLastError();

    [LibraryImport(LIBRARY)]
    public static partial int FPDF_GetPageCount(IntPtr document);

    [LibraryImport(LIBRARY)]
    public static partial int FPDF_GetSecurityHandlerRevision(IntPtr document);

    [LibraryImport(LIBRARY)]
    public static partial int FPDF_GetSignatureCount(IntPtr document);

    [LibraryImport(LIBRARY)]
    public static partial int FPDF_SaveAsCopy(IntPtr document, FileWrite* fileWrite, CULong flags);

    [LibraryImport(LIBRARY)]
    public static partial IntPtr FPDF_LoadPage(IntPtr document, int pageIndex);

    [LibraryImport(LIBRARY)]
    public static partial void FPDF_ClosePage(IntPtr page);

    [LibraryImport(LIBRARY)]
    public static partial float FPDF_GetPageWidthF(IntPtr page);

    [LibraryImport(LIBRARY)]
    public static partial float FPDF_GetPageHeightF(IntPtr page);

    [LibraryImport(LIBRARY)]
    public static partial int FPDF_GetPageBoundingBox(IntPtr page, FsRectF* rect);

    [LibraryImport(LIBRARY)]
    public static partial int FPDFPage_GetRotation(IntPtr page);

    [LibraryImport(LIBRARY)]
    public static partial IntPtr FPDFBitmap_Create(int width, int height, int alpha);

    [LibraryImport(LIBRARY)]
    public static partial int FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, CULong color);

    [LibraryImport(LIBRARY)]
    public static partial void* FPDFBitmap_GetBuffer(IntPtr bitmap);

    [LibraryImport(LIBRARY)]
    public static partial int FPDFBitmap_GetStride(IntPtr bitmap);

    [LibraryImport(LIBRARY)]
    public static partial void FPDFBitmap_Destroy(IntPtr bitmap);

    [LibraryImport(LIBRARY)]
    public static partial void FPDF_RenderPageBitmapWithMatrix(IntPtr bitmap, IntPtr page, FsMatrix* matrix, FsRectF* clipping, int flags);

    [LibraryImport(LIBRARY)]
    public static partial IntPtr FPDFText_LoadPage(IntPtr page);

    [LibraryImport(LIBRARY)]
    public static partial void FPDFText_ClosePage(IntPtr textPage);

    [LibraryImport(LIBRARY)]
    public static partial int FPDFText_CountChars(IntPtr textPage);

    [LibraryImport(LIBRARY)]
    public static partial uint FPDFText_GetUnicode(IntPtr textPage, int index);

    [LibraryImport(LIBRARY)]
    public static partial int FPDFText_GetLooseCharBox(IntPtr textPage, int index, FsRectF* rect);

    [LibraryImport(LIBRARY)]
    public static partial float FPDFText_GetCharAngle(IntPtr textPage, int index);

    [LibraryImport(LIBRARY)]
    public static partial int FPDFText_CountRects(IntPtr textPage, int startIndex, int count);

    [LibraryImport(LIBRARY)]
    public static partial int FPDFText_GetRect(IntPtr textPage, int rectIndex, double* left, double* top, double* right, double* bottom);
}

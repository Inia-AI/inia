namespace IniaAi.FileFormats.Excel;

/// <summary>
/// Opens Excel workbooks for the document pipeline. A workbook that still needs its password fails
/// with <see cref="Unlock.DocumentPasswordProtectedException"/> naming <c>documentName</c>.
/// </summary>
public interface IWorkbookFormat
{
    /// <param name="stream">Open takes it over: the document disposes it, or Open does when it fails.</param>
    IWorkbookDocument Open(Stream stream, string documentName);
}

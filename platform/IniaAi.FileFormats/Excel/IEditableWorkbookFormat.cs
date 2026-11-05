namespace IniaAi.FileFormats.Excel;

/// <summary>Opens the workbooks a workflow writes: a template it fills, or a workbook a user uploaded.</summary>
public interface IEditableWorkbookFormat
{
    /// <summary>Opens the workbook in the bytes. It fails when the bytes hold no workbook it reads.</summary>
    IEditableWorkbook Open(byte[] workbookBytes);

    /// <summary>
    /// The extension of the file that <see cref="IEditableWorkbook.Save"/> writes for the workbook in the bytes:
    /// ".xlsm" for a macro-enabled workbook and ".xlsx" for any other, or null when <see cref="Open"/> cannot read
    /// the bytes or <see cref="IEditableWorkbook.Save"/> cannot keep what they hold. It reads the bytes, never a
    /// file name, which can lie.
    /// </summary>
    string? SaveExtension(byte[] workbookBytes);
}

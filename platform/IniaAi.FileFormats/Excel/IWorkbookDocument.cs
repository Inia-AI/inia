namespace IniaAi.FileFormats.Excel;

/// <summary>An open workbook, with its sheets in workbook order, hidden ones included.</summary>
public interface IWorkbookDocument : IDisposable
{
    IReadOnlyList<IWorkbookSheet> Sheets { get; }
}

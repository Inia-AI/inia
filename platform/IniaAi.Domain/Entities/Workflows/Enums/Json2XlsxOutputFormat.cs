namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>
/// Output document format for an inject-direction Json2Xlsx block. <see cref="Xlsx"/> (default)
/// produces the generated spreadsheet. <see cref="XlsxAndWord"/> additionally renders that spreadsheet
/// to a Word document. Ignored for the extract direction.
/// </summary>
public enum Json2XlsxOutputFormat
{
    Xlsx = 0,
    XlsxAndWord = 1,
}

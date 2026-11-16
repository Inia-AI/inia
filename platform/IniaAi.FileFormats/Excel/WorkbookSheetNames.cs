namespace IniaAi.FileFormats.Excel;

/// <summary>
/// The names of every sheet an Excel package lists, hidden and very hidden ones included, read from the start of its
/// workbook part without loading the part or any sheet, for a check that needs only the names.
/// </summary>
public static class WorkbookSheetNames
{
    /// <exception cref="InvalidDataException">The stream holds no Excel package.</exception>
    /// <exception cref="System.Xml.XmlException">The package's workbook part is not well-formed, or declares a document type.</exception>
    public static IReadOnlySet<string> Read(Stream workbook)
    {
        return WorkbookPackage.SheetNames(workbook);
    }
}

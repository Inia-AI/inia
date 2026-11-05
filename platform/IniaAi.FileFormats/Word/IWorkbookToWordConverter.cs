namespace IniaAi.FileFormats.Word;

/// <summary>Turns a filled workbook into a Word document for a reader who does not open Excel.</summary>
public interface IWorkbookToWordConverter
{
    /// <summary>
    /// The workbook as a .docx. <see cref="WordPageSheets.Select"/> picks the sheets, and each becomes a section that
    /// starts on a new page and holds one table of the values as Excel shows them, without hidden rows and columns.
    /// <list type="bullet">
    /// <item>Every page is US Letter with 1-inch margins and a "Page X of Y" footer at the bottom left, the first page
    /// of a section included.</item>
    /// <item>A table wider than the margins shrinks to them and keeps the proportions of its columns, and its rows grow
    /// to show all their text. A sheet set up for print, with a fit-to-page scale or a zoom other than 100%, keeps
    /// that scale and its row heights instead.</item>
    /// <item>A cell's Excel indent becomes a paragraph indent of 14.4 pt per level.</item>
    /// <item>Markdown in a cell becomes Word formatting, with lists as
    /// <see cref="IWordTemplateParagraph.ReplaceWithMarkdown"/> writes them.</item>
    /// </list>
    /// It fails with an <see cref="InvalidOperationException"/> when the workbook has no visible sheet.
    /// </summary>
    /// <param name="workbookBytes">The workbook.</param>
    /// <param name="footerNotice">A line in the footer of every page, above "Page X of Y"; null for none.</param>
    byte[] ConvertToWord(byte[] workbookBytes, string? footerNotice = null);
}

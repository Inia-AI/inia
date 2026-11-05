using System.Text;

namespace IniaAi.Common.Text;

/// <summary>
/// Provides utilities for formatting markdown tables.
/// </summary>
public static class MarkdownFormatter
{
    /// <summary>
    /// Formats a markdown table string, optionally aligning columns based on content width.
    /// </summary>
    /// <param name="markdownInput">The raw markdown table string.</param>
    /// <param name="alignColumns">If true, pads cells with spaces to align columns visually. Default is true.</param>
    /// <returns>The formatted markdown table.</returns>
    public static string FormatMarkdownTable(string markdownInput, bool alignColumns = true)
    {
        try
        {
            markdownInput = markdownInput.Replace("||", "| |").Trim();

            string[] lines = markdownInput.Split(
                ["\n", "\r", "\r\n"],
                StringSplitOptions.RemoveEmptyEntries);

            var table = lines.Select(line => line.Trim('|').Split('|')).ToList();

            int[] columnLengths = new int[table[0].Length];

            if (alignColumns)
            {
                for (int col = 0; col < table[0].Length; col++)
                {
                    columnLengths[col] = table.Max(row => row[col].Trim().Length == 0
                        ? 1
                        : row[col].Trim().Length);
                }
            }

            var formattedTable = new StringBuilder();

            for (int rowIndex = 0; rowIndex < table.Count; rowIndex++)
            {
                _ = formattedTable.Append('|');

                for (int colIndex = 0; colIndex < table[rowIndex].Length; colIndex++)
                {
                    string cell = table[rowIndex][colIndex].Trim();
                    cell = string.IsNullOrWhiteSpace(cell) ? " " : cell;

                    if (cell.All(c => c is '-' or ' ') && cell.Contains("---"))
                    {
                        _ = formattedTable.Append(alignColumns
                            ? new string('-', columnLengths[colIndex])
                            : "---");
                    }
                    else
                    {
                        _ = formattedTable.Append(cell);

                        if (alignColumns)
                        {
                            int padding = columnLengths[colIndex] - cell.Length;
                            _ = formattedTable.Append(new string(' ', padding));
                        }
                    }

                    _ = formattedTable.Append('|');
                }

                _ = formattedTable.AppendLine();
            }

            return formattedTable.ToString().Trim();
        }
        catch (Exception)
        {
            return markdownInput;
        }
    }
}

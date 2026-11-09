using CsvHelper;
using CsvHelper.Configuration;
using IniaAi.FileFormats.Excel;
using System.Globalization;
using System.Text;

namespace IniaAi.FileFormats.Pages;

/// <summary>
/// Reads a CSV file into pages of <see cref="ExcelUtils.ROWS_PER_PAGE"/> rows, each a Markdown table under the file's
/// first row, the form a workbook sheet's page takes. The delimiter is detected. A row with more fields than the header
/// adds a numbered column, up to the <see cref="ExcelUtils.MAX_COLUMNS"/> columns a workbook page reads; a pipe in a
/// cell is escaped, and a line break in one becomes a space. A file always gives at least one page: an empty one when it
/// has no rows at all. A line longer than <see cref="MAX_ROW_CHARACTERS"/> stops the read, because the parser holds a
/// whole line, and every field of it, in memory.
/// </summary>
public static class CsvPages
{
    public const int MAX_ROW_CHARACTERS = 1_000_000;

    private const string SEPARATOR_CELL = "---";
    private const string EMPTY_CELL = " ";

    public static IEnumerable<string> Read(TextReader reader)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = false,
            DetectDelimiter = true,
            BadDataFound = null,
            MissingFieldFound = null,
        };
        using var parser = new CsvParser(new LineLimitReader(reader), configuration);

        string[]? header = null;
        List<string[]> rows = [];
        while (parser.Read())
        {
            string[] record = parser.Record ?? [];
            if (record.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (header is null)
            {
                header = record;
                continue;
            }

            if (rows.Count == ExcelUtils.ROWS_PER_PAGE)
            {
                yield return Page(header, rows);
                rows.Clear();
            }

            rows.Add(record);
        }

        yield return header is null ? string.Empty : Page(header, rows);
    }

    private static string Page(string[] header, List<string[]> rows)
    {
        int columns = header.Length;
        foreach (string[] row in rows)
        {
            columns = Math.Max(columns, row.Length);
        }

        columns = Math.Min(columns, ExcelUtils.MAX_COLUMNS);

        var page = new StringBuilder();
        _ = page.Append('|');
        for (int column = 0; column < columns; column++)
        {
            _ = page.Append(column < header.Length ? Cell(header[column]) : $"Column {column + 1}").Append('|');
        }

        _ = page.Append("\n|");
        for (int column = 0; column < columns; column++)
        {
            _ = page.Append(SEPARATOR_CELL).Append('|');
        }

        foreach (string[] row in rows)
        {
            _ = page.Append("\n|");
            for (int column = 0; column < columns; column++)
            {
                _ = page.Append(column < row.Length ? Cell(row[column]) : EMPTY_CELL).Append('|');
            }
        }

        return page.ToString();
    }

    private static string Cell(string value)
    {
        string cell = value.ReplaceLineEndings(" ").Trim().Replace("|", "\\|", StringComparison.Ordinal);
        return cell.Length == 0 ? EMPTY_CELL : cell;
    }

    private sealed class LineLimitReader(TextReader inner) : TextReader
    {
        private int _lineLength;

        public override int Peek()
        {
            return inner.Peek();
        }

        public override int Read()
        {
            int character = inner.Read();
            if (character == '\n')
            {
                _lineLength = 0;
            }
            else if (character >= 0)
            {
                Check(++_lineLength);
            }

            return character;
        }

        public override int Read(char[] buffer, int index, int count)
        {
            int read = inner.Read(buffer, index, count);
            Measure(buffer.AsSpan(index, read));
            return read;
        }

        public override int Read(Span<char> buffer)
        {
            int read = inner.Read(buffer);
            Measure(buffer[..read]);
            return read;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }

        private void Measure(ReadOnlySpan<char> characters)
        {
            int lineBreak;
            while ((lineBreak = characters.IndexOf('\n')) >= 0)
            {
                Check(_lineLength + lineBreak);
                _lineLength = 0;
                characters = characters[(lineBreak + 1)..];
            }

            Check(_lineLength += characters.Length);
        }

        private static void Check(int lineLength)
        {
            if (lineLength > MAX_ROW_CHARACTERS)
            {
                throw new InvalidDataException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The CSV file has a row longer than {MAX_ROW_CHARACTERS:N0} characters, which the platform does not read. Split the row or save the file with fewer columns, then upload it again."));
            }
        }
    }
}

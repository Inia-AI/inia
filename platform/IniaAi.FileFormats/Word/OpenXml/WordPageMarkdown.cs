using System.Text;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>A piece of paragraph text with the emphasis the Markdown keeps.</summary>
internal sealed record StyledText(string Text, bool Bold, bool Italic);

internal enum ListKind
{
    None,
    Bullet,
    Numbered,
}

/// <summary>Writes the Markdown of one paragraph or one table, the shape the paid reader gives ingestion.</summary>
internal static class WordPageMarkdown
{
    public static string Paragraph(IReadOnlyList<StyledText> pieces, int headingLevel, ListKind list, int listLevel, string listLabel)
    {
        string text = Emphasis(pieces).Trim();
        if (text.Length == 0)
        {
            return string.Empty;
        }

        if (list == ListKind.Numbered && listLabel.Length > 0)
        {
            text = $"{listLabel} {text}";
        }

        if (headingLevel is >= 1 and <= 6)
        {
            return $"{new string('#', headingLevel)} {text.Replace('\n', ' ')}";
        }

        return list switch
        {
            ListKind.Bullet => $"{new string(' ', listLevel * 2)}- {text}",
            ListKind.Numbered => $"{new string(' ', listLevel * 3)}{text}",
            _ => text,
        };
    }

    public static string Table(IReadOnlyList<IReadOnlyList<string>> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        int width = rows.Max(row => row.Count);
        if (width == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (int r = 0; r < rows.Count; r++)
        {
            _ = builder.Append('|');
            for (int c = 0; c < width; c++)
            {
                string cell = c < rows[r].Count ? rows[r][c] : string.Empty;
                _ = builder.Append(' ').Append(Cell(cell)).Append(" |");
            }

            _ = builder.Append('\n');
            if (r == 0)
            {
                _ = builder.Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", width))).Append('\n');
            }
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string Cell(string text)
    {
        return text.Trim().Replace("|", "\\|").Replace("\r\n", "<br>").Replace("\n", "<br>");
    }

    private static string Emphasis(IReadOnlyList<StyledText> pieces)
    {
        var builder = new StringBuilder();
        int i = 0;
        while (i < pieces.Count)
        {
            bool bold = pieces[i].Bold;
            bool italic = pieces[i].Italic;
            var run = new StringBuilder();
            while (i < pieces.Count && pieces[i].Bold == bold && pieces[i].Italic == italic)
            {
                _ = run.Append(pieces[i].Text);
                i++;
            }

            _ = builder.Append(Wrap(run.ToString(), bold, italic));
        }

        return builder.ToString();
    }

    private static string Wrap(string text, bool bold, bool italic)
    {
        string core = text.Trim();
        if (core.Length == 0 || (!bold && !italic))
        {
            return text;
        }

        string marker = (bold ? "**" : string.Empty) + (italic ? "*" : string.Empty);
        string lead = text[..text.IndexOf(core, StringComparison.Ordinal)];
        string trail = text[(lead.Length + core.Length)..];
        return $"{lead}{marker}{core}{marker}{trail}";
    }
}

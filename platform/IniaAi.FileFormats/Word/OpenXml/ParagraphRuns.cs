using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Wordprocessing;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>
/// The text of a paragraph's own runs, without the paragraphs of a text box inside it, edited in place: a replacement
/// takes the formatting of the run where its match starts, also when the match spans differently formatted runs.
/// </summary>
internal static class ParagraphRuns
{
    private const char LINE_BREAK = '\n';
    private const char TAB = '\t';

    public static string Text(Paragraph paragraph)
    {
        return string.Concat(Pieces(paragraph).Select(piece => piece.Text));
    }

    /// <summary>Replaces each match in the paragraph's text; a "\n" in a replacement becomes a line break in its run.</summary>
    public static void Replace(Paragraph paragraph, Regex pattern, Func<Match, string> replacement)
    {
        List<(Match Match, string Text)> replacements = [.. pattern.Matches(Text(paragraph)).Select(match => (match, replacement(match)))];

        // From the last match to the first, so an edit never moves the text of a match still to come.
        for (int i = replacements.Count - 1; i >= 0; i--)
        {
            (Match match, string text) = replacements[i];
            ReplaceAt(Pieces(paragraph), match.Index, match.Length, text.Replace("\r\n", "\n", StringComparison.Ordinal));
        }
    }

    private static void ReplaceAt(List<Piece> pieces, int start, int length, string replacement)
    {
        int end = start + length;
        List<Piece> covered = [.. pieces.Where(piece => piece.Start < end && piece.End > start)];
        if (covered.Count == 0)
        {
            covered = [.. pieces.Where(piece => piece.Start <= start && piece.End >= start).Take(1)];
            if (covered.Count == 0)
            {
                return;
            }
        }

        Text? target = covered.Select(piece => piece.Element).OfType<Text>().FirstOrDefault();
        if (target is null)
        {
            target = new Text();
            covered[0].Element.InsertAfterSelf(target);
        }

        foreach (Piece piece in covered)
        {
            if (piece.Element is not Text text)
            {
                piece.Element.Remove();
                continue;
            }

            string before = piece.Start < start ? piece.Text[..(start - piece.Start)] : string.Empty;
            string after = piece.End > end ? piece.Text[(end - piece.Start)..] : string.Empty;
            Write(text, text == target ? before + replacement + after : before + after);
        }
    }

    private static void Write(Text text, string value)
    {
        string[] lines = value.Split(LINE_BREAK);
        text.Text = lines[0];
        text.Space = SpaceProcessingModeValues.Preserve;
        OpenXmlElement last = text;
        foreach (string line in lines.Skip(1))
        {
            var lineBreak = new Break();
            last.InsertAfterSelf(lineBreak);
            last = lineBreak.InsertAfterSelf(new Text(line) { Space = SpaceProcessingModeValues.Preserve });
        }
    }

    private static List<Piece> Pieces(Paragraph paragraph)
    {
        var pieces = new List<Piece>();
        int position = 0;
        foreach (OpenXmlElement element in paragraph.Descendants().Where(element => element.Ancestors<Paragraph>().First() == paragraph))
        {
            string? text = element switch
            {
                Text run => run.Text,
                Break or CarriageReturn => LINE_BREAK.ToString(),
                TabChar => TAB.ToString(),
                _ => null,
            };
            if (text is not null)
            {
                pieces.Add(new Piece(element, text, position));
                position += text.Length;
            }
        }

        return pieces;
    }

    private sealed record Piece(OpenXmlElement Element, string Text, int Start)
    {
        public int End => Start + Text.Length;
    }
}

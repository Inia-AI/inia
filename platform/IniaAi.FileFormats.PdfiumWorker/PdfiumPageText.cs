using System.Text;

namespace IniaAi.FileFormats.Pdf.Pdfium;

/// <summary>
/// A page's text in the order it reads on the page, laid out the way Aspose's pure text mode lays
/// it out: rows from the top of the page as displayed, each run of characters at its column on a
/// grid one median character wide. A run of rotated characters, such as a chart's vertical label,
/// stays one run in the order the file draws it. PDFium's own text follows the order the file
/// draws in, which can put a page's header last.
/// </summary>
internal static unsafe class PdfiumPageText
{
    private const double RUN_GAP_IN_CHARACTERS = 2.0;

    /// <summary>No real line is wider; a crafted page with tiny or zero-width glyphs would otherwise ask for millions of spaces.</summary>
    private const int MAX_COLUMN = 1000;
    private const double BLANK_LINE_GAP_IN_ROWS = 1.0;
    private const double SAME_ANGLE = 0.05;
    private const double VERTICAL_SINE = 0.5;

    public static string Read(PdfiumPage page, IntPtr textPage, PageBox? area)
    {
        List<Glyph> glyphs = Glyphs(page, textPage, area);
        double characterWidth = MedianWidth(glyphs);
        if (characterWidth <= 0)
        {
            return string.Empty;
        }

        return Layout(Rows(Runs(glyphs, characterWidth)), characterWidth);
    }

    private static List<Glyph> Glyphs(PdfiumPage page, IntPtr textPage, PageBox? area)
    {
        int count = PdfiumNative.FPDFText_CountChars(textPage);
        var glyphs = new List<Glyph>(Math.Max(count, 0));

        for (int index = 0; index < count; index++)
        {
            int codePoint = (int)PdfiumNative.FPDFText_GetUnicode(textPage, index);
            if (codePoint is '\r' or '\n')
            {
                glyphs.Add(Glyph.LINE_BREAK);
                continue;
            }

            PdfiumNative.FsRectF box = default;
            bool placed = !IsBlank(codePoint)
                && PdfiumNative.FPDFText_GetLooseCharBox(textPage, index, &box) != 0
                && box.Right > box.Left
                && box.Top > box.Bottom;

            if (!placed)
            {
                glyphs.Add(new Glyph(codePoint, 0, 0, 0, 0, IsPlaced: false, BreaksLine: false, Angle: 0));
                continue;
            }

            if (!(area ?? page.Box).Contains((box.Left + box.Right) / 2, (box.Top + box.Bottom) / 2))
            {
                glyphs.Add(Glyph.LINE_BREAK);
                continue;
            }

            (double left, double top, double right, double bottom) = page.ToDisplay(new PageBox(box.Left, box.Bottom, box.Right, box.Top));
            double angle = PdfiumNative.FPDFText_GetCharAngle(textPage, index) - (page.Rotation * Math.PI / 2);
            glyphs.Add(new Glyph(codePoint, left, top, right, bottom, IsPlaced: true, BreaksLine: false, angle));
        }

        return glyphs;
    }

    private static double MedianWidth(List<Glyph> glyphs)
    {
        double[] widths = [.. glyphs.Where(glyph => glyph.IsPlaced && !glyph.IsVertical).Select(glyph => glyph.Right - glyph.Left)];
        if (widths.Length == 0)
        {
            return 0;
        }

        Array.Sort(widths);
        return widths[widths.Length / 2];
    }

    private static List<Run> Runs(List<Glyph> glyphs, double characterWidth)
    {
        var runs = new List<Run>();
        RunBuilder? current = null;

        foreach (Glyph glyph in glyphs)
        {
            if (glyph.BreaksLine)
            {
                Close(current, runs);
                current = null;
            }
            else if (!glyph.IsPlaced)
            {
                current?.Append(glyph.CodePoint);
            }
            else
            {
                if (current is null || !current.Continues(glyph, characterWidth))
                {
                    Close(current, runs);
                    current = new RunBuilder();
                }

                current.Add(glyph);
            }
        }

        Close(current, runs);
        return runs;
    }

    private static void Close(RunBuilder? builder, List<Run> runs)
    {
        if (builder?.Build() is Run run)
        {
            runs.Add(run);
        }
    }

    private static List<Row> Rows(List<Run> runs)
    {
        var rows = new List<Row>();

        foreach (Run run in runs.OrderBy(run => run.Middle))
        {
            if (rows.Count == 0 || run.Middle > rows[^1].Bottom)
            {
                rows.Add(new Row(run.Middle - (run.Thickness / 2), run.Middle + (run.Thickness / 2)));
            }

            rows[^1].Runs.Add(run);
        }

        return rows;
    }

    private static string Layout(List<Row> rows, double characterWidth)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        double origin = rows.Min(row => row.Runs.Min(run => run.Left));
        var text = new StringBuilder();
        Row? previous = null;

        foreach (Row row in rows)
        {
            if (previous is not null)
            {
                _ = text.Append('\n');
                if (row.Top - previous.Bottom > BLANK_LINE_GAP_IN_ROWS * (previous.Bottom - previous.Top))
                {
                    _ = text.Append('\n');
                }
            }

            int lineStart = text.Length;
            foreach (Run run in row.Runs.OrderBy(run => run.Left))
            {
                double position = Math.Round((run.Left - origin) / characterWidth);
                int column = double.IsFinite(position) ? (int)Math.Clamp(position, 0, MAX_COLUMN) : 0;
                int written = text.Length - lineStart;
                _ = text.Append(' ', written == 0 ? column : Math.Max(1, column - written)).Append(run.Text);
            }

            previous = row;
        }

        return text.ToString();
    }

    private static bool IsBlank(int codePoint)
    {
        return codePoint <= char.MaxValue && char.IsWhiteSpace((char)codePoint);
    }

    /// <summary>One character in display space, top-left origin, y growing down, with its angle on the page as displayed.</summary>
    private readonly record struct Glyph(int CodePoint, double Left, double Top, double Right, double Bottom, bool IsPlaced, bool BreaksLine, double Angle)
    {
        public static readonly Glyph LINE_BREAK = new(0, 0, 0, 0, 0, IsPlaced: false, BreaksLine: true, Angle: 0);

        public double Middle => (Top + Bottom) / 2;

        public bool IsVertical => Math.Abs(Math.Sin(Angle)) > VERTICAL_SINE;
    }

    /// <param name="Thickness">The height of a line of the run as displayed: its width when its characters run up or down.</param>
    private sealed record Run(string Text, double Left, double Top, double Bottom, double Thickness)
    {
        public double Middle => (Top + Bottom) / 2;
    }

    private sealed record Row(double Top, double Bottom)
    {
        public List<Run> Runs { get; } = [];
    }

    private sealed class RunBuilder
    {
        private readonly StringBuilder _text = new();
        private double _left = double.MaxValue;
        private double _top = double.MaxValue;
        private double _right = double.MinValue;
        private double _bottom = double.MinValue;
        private Glyph? _last;

        /// <summary>
        /// True when <paramref name="glyph"/> sits on this run's line, close enough after its last
        /// character. A rotated run continues with the next character at the same angle beside its last.
        /// </summary>
        public bool Continues(Glyph glyph, double characterWidth)
        {
            if (_last is not Glyph last || glyph.IsVertical != last.IsVertical)
            {
                return false;
            }

            double reach = RUN_GAP_IN_CHARACTERS * characterWidth;
            if (glyph.IsVertical)
            {
                return Math.Abs(glyph.Angle - last.Angle) < SAME_ANGLE
                    && Math.Max(glyph.Left - last.Right, last.Left - glyph.Right) <= reach
                    && Math.Max(glyph.Top - last.Bottom, last.Top - glyph.Bottom) <= reach;
            }

            return glyph.Middle >= _top
                && glyph.Middle <= _bottom
                && glyph.Left >= _right - characterWidth
                && glyph.Left - _right <= reach;
        }

        public void Add(Glyph glyph)
        {
            _last = glyph;
            Append(glyph.CodePoint);
            _left = Math.Min(_left, glyph.Left);
            _top = Math.Min(_top, glyph.Top);
            _right = Math.Max(_right, glyph.Right);
            _bottom = Math.Max(_bottom, glyph.Bottom);
        }

        public void Append(int codePoint)
        {
            if (codePoint <= char.MaxValue)
            {
                _ = _text.Append((char)codePoint);
            }
            else if (Rune.IsValid(codePoint))
            {
                _ = _text.Append(char.ConvertFromUtf32(codePoint));
            }
        }

        public Run? Build()
        {
            string text = _text.ToString().Trim();
            double thickness = _last is { IsVertical: true } ? _right - _left : _bottom - _top;
            return text.Length == 0 ? null : new Run(text, _left, _top, _bottom, thickness);
        }
    }
}

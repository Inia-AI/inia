using System.Text;

namespace IniaAi.FileFormats.Pages;

/// <summary>
/// Splits a text or Markdown file into pages of at most <see cref="PAGE_CHARACTERS"/>, about what a page of an annual
/// report holds, closing a page at a paragraph break. In Markdown a heading opens a new page once the page is half full,
/// and a fenced code block is never split at its blank lines. A paragraph longer than a page splits at its last line
/// break that fits, else at its last space, else at the page's length. A file that fits on one page stays as it is.
/// </summary>
public static class TextPages
{
    public const int PAGE_CHARACTERS = 4_000;

    private const string PARAGRAPH_BREAK = "\n\n";
    private const string SOFT_BREAKS = "\n ";
    private static readonly string[] _fences = ["```", "~~~"];

    public static IReadOnlyList<string> Split(string text, bool markdown)
    {
        return text.Length <= PAGE_CHARACTERS ? [text] : [.. Read(new StringReader(text), markdown)];
    }

    /// <summary>The pages of a longer text, read line by line, so the text is never held whole.</summary>
    public static IEnumerable<string> Read(TextReader reader, bool markdown)
    {
        var page = new StringBuilder();
        foreach (string block in Blocks(reader, markdown))
        {
            bool headingOpensPage = markdown && IsHeading(block) && page.Length >= PAGE_CHARACTERS / 2;
            if (page.Length > 0 && (headingOpensPage || page.Length + PARAGRAPH_BREAK.Length + block.Length > PAGE_CHARACTERS))
            {
                yield return page.ToString();
                _ = page.Clear();
            }

            if (block.Length > PAGE_CHARACTERS)
            {
                int start = 0;
                while (block.Length - start > PAGE_CHARACTERS)
                {
                    int cut = CutOf(block, start);
                    yield return block[start..cut];
                    start = SOFT_BREAKS.Contains(block[cut]) ? cut + 1 : cut;
                }

                _ = page.Append(block, start, block.Length - start);
                continue;
            }

            if (page.Length > 0)
            {
                _ = page.Append(PARAGRAPH_BREAK);
            }

            _ = page.Append(block);
        }

        if (page.Length > 0)
        {
            yield return page.ToString();
        }
    }

    private static IEnumerable<string> Blocks(TextReader reader, bool markdown)
    {
        var block = new StringBuilder();
        string? fence = null;
        while (reader.ReadLine() is { } line)
        {
            string trimmed = line.TrimStart();
            string? fenceMark = markdown ? Array.Find(_fences, mark => trimmed.StartsWith(mark, StringComparison.Ordinal)) : null;
            bool breaksBlock = fence is null && (string.IsNullOrWhiteSpace(line) || (markdown && IsHeading(trimmed)));
            if (breaksBlock && block.Length > 0)
            {
                yield return block.ToString().TrimEnd();
                _ = block.Clear();
            }

            if (fenceMark is not null && (fence is null || fence == fenceMark))
            {
                fence = fence is null ? fenceMark : null;
            }

            if (!string.IsNullOrWhiteSpace(line) || fence is not null)
            {
                if (block.Length > 0)
                {
                    _ = block.Append('\n');
                }

                _ = block.Append(line);
            }
        }

        if (block.Length > 0)
        {
            yield return block.ToString().TrimEnd();
        }
    }

    private static int CutOf(string block, int start)
    {
        foreach (char softBreak in SOFT_BREAKS)
        {
            int at = block.LastIndexOf(softBreak, start + PAGE_CHARACTERS, PAGE_CHARACTERS);
            if (at > start)
            {
                return at;
            }
        }

        return start + PAGE_CHARACTERS;
    }

    private static bool IsHeading(string block)
    {
        int level = 0;
        while (level < block.Length && block[level] == '#')
        {
            level++;
        }

        return level is >= 1 and <= 6 && level < block.Length && block[level] == ' ';
    }
}

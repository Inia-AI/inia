namespace IniaAi.Core.Documents;

/// <summary>Why a page's text layer cannot stand in for the page itself.</summary>
public enum PageTextFault
{
    Blank,
    TooShort,
    ControlCharacters,
    ShortWords,
    ImplausiblyLongWords,
    ScrambledLine,
}

/// <summary>
/// Whether a page's extracted text can stand in for the page. Document processing sends a page
/// with a fault to OCR; any other reader that must see such a page uses the same test.
/// </summary>
public static class PageTextQuality
{
    private const int MIN_PAGE_TEXT_LENGTH = 15;
    private const double MAX_CONTROL_CHARACTER_RATIO = 0.1;
    private const double MIN_AVG_WORD_LENGTH = 2.5;
    private const double MAX_PLAUSIBLE_AVG_WORD_LENGTH = 15;
    private const int MIN_WORDS_FOR_WORD_LENGTH_CHECK = 5;
    private const double MIN_LETTER_SHARE_PER_WORD = 0.3;
    private const double MIN_LATIN_LETTER_RATIO = 0.9;
    private const char LATIN_SCRIPT_MAX_CHAR = '\u024F';
    private const char LATIN_EXTENDED_MIN_CHAR = '\u0100';
    private const int MIN_LETTERS_FOR_SCRAMBLED_LINE = 8;
    private const double MIN_LATIN_EXTENDED_SHARE_OF_SCRAMBLED_LINE = 0.4;
    private const int MIN_CONTROL_CHARS_AT_LETTERS_OF_SCRAMBLED_LINE = 2;
    private const int MIN_LATIN_EXTENDED_LETTERS_BESIDE_CONTROL_CHARS = 4;
    private const char LATIN_ACCENTED_MIN_CHAR = '\u00C0';
    private const int MIN_LETTERS_FOR_SCRAMBLED_WORD = 6;
    private const int MIN_LATIN_EXTENDED_LETTERS_OF_SCRAMBLED_WORD = 3;
    private const double MIN_ACCENTED_SHARE_OF_SCRAMBLED_WORD = 0.5;
    private const int MIN_CASE_FLIPS_OF_SCRAMBLED_WORD = 2;

    private static readonly char[] WORD_TRIM_CHARS = ['.', '\u2026'];

    /// <summary>The first fault the text shows, or null when it can stand in for the page.</summary>
    public static PageTextFault? FaultOf(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return PageTextFault.Blank;
        }

        if (text.Length < MIN_PAGE_TEXT_LENGTH)
        {
            return PageTextFault.TooShort;
        }

        if (ControlCharacterRatio(text) > MAX_CONTROL_CHARACTER_RATIO)
        {
            return PageTextFault.ControlCharacters;
        }

        if (AvgWordLengthIsSuspicious(text))
        {
            return PageTextFault.ShortWords;
        }

        if (AvgWordLengthIsImplausiblyLong(text))
        {
            return PageTextFault.ImplausiblyLongWords;
        }

        if (HasScrambledLine(text))
        {
            return PageTextFault.ScrambledLine;
        }

        return null;
    }

    public static double ControlCharacterRatio(string text)
    {
        return text.Count(IsNonWhitespaceControl) / (double)text.Length;
    }

    /// <summary>
    /// A font with no Unicode map extracts as letters from Latin Extended (U+0100–U+024F) with a
    /// control character where each space should be: "November 2025" reads "cŅƴåĵÆåų\u0003ƖǈƖĂ".
    /// No language writes most of a line in those letters, and a line that mixes clean words with
    /// a scrambled run still shows control characters against Latin Extended letters, or one
    /// scrambled word: six or more letters, mostly accented, several from Latin Extended, and
    /// switching from lower to upper case at least twice inside the word. Turkish "ağırlığı" and
    /// Czech "řečiště" are as accented, but a word in any language keeps its case. Control
    /// characters against plain letters alone are not enough: some fonts map only the space to
    /// one, and their words read correctly. One such line is enough: a page can mix a clean font
    /// with a scrambled one, and the scrambled lines are often the figures.
    /// </summary>
    private static bool HasScrambledLine(string text)
    {
        foreach (ReadOnlySpan<char> line in text.AsSpan().EnumerateLines())
        {
            if (IsScrambledLine(line))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsScrambledLine(ReadOnlySpan<char> line)
    {
        int letters = 0;
        int latinExtendedLetters = 0;
        int controlCharsAtLetters = 0;

        for (int index = 0; index < line.Length; index++)
        {
            char c = line[index];

            if (char.IsLetter(c))
            {
                letters++;
                latinExtendedLetters += c is >= LATIN_EXTENDED_MIN_CHAR and <= LATIN_SCRIPT_MAX_CHAR ? 1 : 0;
            }
            else if (IsNonWhitespaceControl(c)
                && ((index > 0 && char.IsLetter(line[index - 1]))
                    || (index + 1 < line.Length && char.IsLetter(line[index + 1]))))
            {
                controlCharsAtLetters++;
            }
        }

        return (letters >= MIN_LETTERS_FOR_SCRAMBLED_LINE
                && latinExtendedLetters > letters * MIN_LATIN_EXTENDED_SHARE_OF_SCRAMBLED_LINE)
            || (controlCharsAtLetters >= MIN_CONTROL_CHARS_AT_LETTERS_OF_SCRAMBLED_LINE
                && latinExtendedLetters >= MIN_LATIN_EXTENDED_LETTERS_BESIDE_CONTROL_CHARS)
            || (latinExtendedLetters >= MIN_LATIN_EXTENDED_LETTERS_OF_SCRAMBLED_WORD && HasScrambledWord(line));
    }

    private static bool HasScrambledWord(ReadOnlySpan<char> line)
    {
        int letters = 0;
        int accentedLetters = 0;
        int latinExtendedLetters = 0;
        int caseFlips = 0;
        bool previousLetterIsLower = false;

        for (int index = 0; index <= line.Length; index++)
        {
            if (index == line.Length || char.IsWhiteSpace(line[index]))
            {
                if (letters >= MIN_LETTERS_FOR_SCRAMBLED_WORD
                    && latinExtendedLetters >= MIN_LATIN_EXTENDED_LETTERS_OF_SCRAMBLED_WORD
                    && accentedLetters > letters * MIN_ACCENTED_SHARE_OF_SCRAMBLED_WORD
                    && caseFlips >= MIN_CASE_FLIPS_OF_SCRAMBLED_WORD)
                {
                    return true;
                }

                letters = accentedLetters = latinExtendedLetters = caseFlips = 0;
                previousLetterIsLower = false;
                continue;
            }

            char c = line[index];
            if (char.IsLetter(c))
            {
                letters++;
                accentedLetters += c is >= LATIN_ACCENTED_MIN_CHAR and <= LATIN_SCRIPT_MAX_CHAR ? 1 : 0;
                latinExtendedLetters += c is >= LATIN_EXTENDED_MIN_CHAR and <= LATIN_SCRIPT_MAX_CHAR ? 1 : 0;
                caseFlips += previousLetterIsLower && char.IsUpper(c) ? 1 : 0;
                previousLetterIsLower = char.IsLower(c);
            }
        }

        return false;
    }

    private static bool IsNonWhitespaceControl(char c)
    {
        return char.IsControl(c) && !char.IsWhiteSpace(c);
    }

    private static bool AvgWordLengthIsSuspicious(string text)
    {
        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return true;
        }

        double avg = words.Average(w => w.Length);
        return avg < MIN_AVG_WORD_LENGTH;
    }

    private static bool AvgWordLengthIsImplausiblyLong(string text)
    {
        // Only meaningful for space-delimited scripts: CJK text legitimately has no
        // spaces (the predecessor PdfObfuscationDetector was removed for false-flagging
        // Japanese documents).
        if (!IsMostlyLatinLetters(text))
        {
            return false;
        }

        // Dot-trimming exempts TOC leader runs glued to their entry ("Japan...... 3");
        // the letter-share floor exempts tokens that are numbers with an incidental
        // letter ("P=11,597,399,876" balance-sheet columns).
        List<string> words = [.. text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Trim(WORD_TRIM_CHARS))
            .Where(w => w.Length > 0 && w.Count(char.IsLetter) >= w.Length * MIN_LETTER_SHARE_PER_WORD)];

        if (words.Count < MIN_WORDS_FOR_WORD_LENGTH_CHECK)
        {
            return false;
        }

        return words.Average(w => w.Length) > MAX_PLAUSIBLE_AVG_WORD_LENGTH;
    }

    private static bool IsMostlyLatinLetters(string text)
    {
        int letters = 0;
        int latinLetters = 0;

        foreach (char c in text)
        {
            if (!char.IsLetter(c))
            {
                continue;
            }

            letters++;

            if (c <= LATIN_SCRIPT_MAX_CHAR)
            {
                latinLetters++;
            }
        }

        if (letters == 0)
        {
            return false;
        }

        return latinLetters >= letters * MIN_LATIN_LETTER_RATIO;
    }
}

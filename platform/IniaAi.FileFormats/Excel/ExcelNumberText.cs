using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ExcelNumberFormat;

namespace IniaAi.FileFormats.Excel;

/// <summary>
/// A number as Excel shows it in a cell of standard width. The General format fits 11 characters,
/// the sign of the value picks the section of a format, an empty section shows nothing, and a
/// halfway value rounds away from zero on its 15 significant digits. ExcelNumberFormat formats the
/// chosen section; it gets none of these right on its own.
/// </summary>
internal static partial class ExcelNumberText
{
    internal const string GENERAL = "General";
    private const double SECONDS_PER_DAY = 86400;
    private const char SECTION_SEPARATOR = ';';
    private const char QUOTE = '"';
    private const char ESCAPE = '\\';
    private const char BRACKET_OPEN = '[';
    private const char BRACKET_CLOSE = ']';
    private const char DECIMAL_POINT = '.';
    private const char THOUSANDS = ',';
    private const char PERCENT = '%';
    private const int GENERAL_WIDTH = 11;
    private const int GENERAL_FIXED_DECIMALS = 12;
    private const int GENERAL_SIGNIFICANT_DIGITS = 10;
    private const int GENERAL_MANTISSA_DECIMALS = 5;
    private const int GENERAL_LAST_FIXED_EXPONENT = 9;
    private const int GENERAL_FIRST_SHORT_EXPONENT = -4;
    private const int GENERAL_ELEVEN_DIGIT_EXPONENT = 10;
    private const double LARGEST_DECIMAL = 7.9e28;
    internal const double FIRST_SERIAL_PAST_YEAR_9999 = 2958466;
    private const double DATE_1904_OFFSET = 1462;
    private const string OUT_OF_RANGE_DATE = "########";
    private const int MAX_CACHED_FORMATS = 512;

    private static readonly ConcurrentDictionary<string, NumberFormat> _parsed = new(StringComparer.Ordinal);

    /// <summary>What to add to a serial of a workbook that counts from 1904 to make it a serial counted from 1900.</summary>
    public static double DateOffset(bool isDate1904)
    {
        return isDate1904 ? DATE_1904_OFFSET : 0;
    }

    public static bool IsDateFormat(string code)
    {
        NumberFormat format = Parsed(code);
        return format.IsValid && (format.IsDateTimeFormat || format.IsTimeSpanFormat);
    }

    public static string Format(double value, string format, bool isDate1904, CultureInfo culture)
    {
        if (string.IsNullOrEmpty(format) || string.Equals(format, GENERAL, StringComparison.OrdinalIgnoreCase))
        {
            return General(value, culture);
        }

        List<string> sections = Sections(format);
        if (sections.Any(HasCondition))
        {
            return Parsed(format).Format(value, culture, isDate1904);
        }

        (string section, double shown) = Choose(sections, value);
        if (section.Length == 0)
        {
            return string.Empty;
        }

        if (string.Equals(section, GENERAL, StringComparison.OrdinalIgnoreCase))
        {
            return General(shown, culture);
        }

        if (IsDateFormat(section))
        {
            return DateText(shown, section, Parsed(section), isDate1904, culture);
        }

        string numberSection = WithLettersEscaped(section);
        NumberFormat numberFormat = Parsed(numberSection);
        return numberFormat.IsValid
            ? numberFormat.Format(RoundedAsShown(shown, numberSection), culture, isDate1904)
            : Parsed(format).Format(value, culture, isDate1904);
    }

    /// <summary>A workbook uses few format codes for many cells, so each is parsed once; the cache stops growing at a bound.</summary>
    private static NumberFormat Parsed(string code)
    {
        if (_parsed.TryGetValue(code, out NumberFormat? format))
        {
            return format;
        }

        format = new NumberFormat(code);
        if (_parsed.Count < MAX_CACHED_FORMATS)
        {
            _ = _parsed.TryAdd(code, format);
        }

        return format;
    }

    /// <summary>
    /// Excel fills a cell with hashes for a date before its first one or past the year 9999. ExcelNumberFormat writes
    /// fractions of a second with three digits whatever the format asks, so the fraction is cut to the digits asked
    /// for, after the value is rounded to them.
    /// </summary>
    private static string DateText(double value, string section, NumberFormat format, bool isDate1904, CultureInfo culture)
    {
        if (value < 0 || value + DateOffset(isDate1904) >= FIRST_SERIAL_PAST_YEAR_9999)
        {
            return OUT_OF_RANGE_DATE;
        }

        Match fraction = SecondFractionRegex().Match(section);
        if (!fraction.Success)
        {
            return format.Format(value, culture, isDate1904);
        }

        int digits = fraction.Groups["digits"].Length;
        double unit = SECONDS_PER_DAY * Math.Pow(10, digits);
        double rounded = Math.Round(value * unit, MidpointRounding.AwayFromZero) / unit;
        string text = format.Format(rounded, culture, isDate1904);
        Match written = WrittenFractionRegex().Match(text);
        return written.Success
            ? text.Remove(written.Groups["digits"].Index + digits, written.Groups["digits"].Length - digits)
            : text;
    }

    /// <summary>Excel shows a letter outside quotes as itself; ExcelNumberFormat needs it escaped. An exponent stays.</summary>
    private static string WithLettersEscaped(string section)
    {
        var escaped = new StringBuilder(section.Length);
        for (int i = 0; i < section.Length; i++)
        {
            int end = LiteralEnd(section, i);
            char character = section[i];
            bool isExponent = character is 'E' or 'e' && i + 1 < section.Length && section[i + 1] is '+' or '-';
            if (end == i && char.IsLetter(character) && !isExponent)
            {
                _ = escaped.Append(ESCAPE);
            }

            _ = escaped.Append(section, i, end - i + 1);
            i = end;
        }

        return escaped.ToString();
    }

    /// <summary>A lone section shows the sign itself; with two or more, the negative section shows the size and its own marks.</summary>
    private static (string Section, double Shown) Choose(List<string> sections, double value)
    {
        if (sections.Count == 1)
        {
            return (sections[0], value);
        }

        if (value < 0)
        {
            return (sections[1], -value);
        }

        return value == 0 && sections.Count > 2 ? (sections[2], value) : (sections[0], value);
    }

    private static List<string> Sections(string format)
    {
        var sections = new List<string>();
        var current = new StringBuilder();
        for (int i = 0; i < format.Length; i++)
        {
            char character = format[i];
            if (character == SECTION_SEPARATOR)
            {
                sections.Add(current.ToString());
                _ = current.Clear();
                continue;
            }

            int end = LiteralEnd(format, i);
            _ = current.Append(format, i, end - i + 1);
            i = end;
        }

        sections.Add(current.ToString());
        return sections;
    }

    /// <summary>The last index of the token that starts at <paramref name="start"/>: a quoted text, an escaped character or a bracketed code count as one.</summary>
    private static int LiteralEnd(string format, int start)
    {
        char character = format[start];
        if (character is ESCAPE or '_' or '*')
        {
            return Math.Min(start + 1, format.Length - 1);
        }

        char close = character switch
        {
            QUOTE => QUOTE,
            BRACKET_OPEN => BRACKET_CLOSE,
            _ => '\0',
        };
        if (close == '\0')
        {
            return start;
        }

        int end = format.IndexOf(close, start + 1);
        return end < 0 ? format.Length - 1 : end;
    }

    private static bool HasCondition(string section)
    {
        string trimmed = section.TrimStart();
        return trimmed.Length > 1 && trimmed[0] == BRACKET_OPEN && trimmed[1] is '<' or '>' or '=';
    }

    /// <summary>
    /// The value rounded half away from zero to the digits the section shows, as Excel rounds it, so that
    /// the formatter's own rounding has no halfway value left to round.
    /// </summary>
    private static double RoundedAsShown(double value, string section)
    {
        if (!TryReadDigits(section, out int decimals, out int percents, out int thousandsScaling) || Math.Abs(value) >= LARGEST_DECIMAL)
        {
            return value;
        }

        decimal scale = Power(100, percents) / Power(1000, thousandsScaling);
        decimal shown = Math.Round((decimal)value * scale, decimals, MidpointRounding.AwayFromZero);
        double rounded = (double)(shown / scale);
        return rounded == 0 ? 0 : rounded;
    }

    /// <summary>
    /// The decimals, percent signs and thousands scaling of a plain number section; false for a section
    /// in scientific notation or with a fraction, which ExcelNumberFormat rounds itself.
    /// </summary>
    private static bool TryReadDigits(string section, out int decimals, out int percents, out int thousandsScaling)
    {
        decimals = 0;
        percents = 0;
        thousandsScaling = 0;
        bool afterPoint = false;
        int lastPlaceholder = -1;
        for (int i = 0; i < section.Length; i++)
        {
            int end = LiteralEnd(section, i);
            if (end != i)
            {
                i = end;
                continue;
            }

            switch (section[i])
            {
                case '0' or '#' or '?':
                    lastPlaceholder = i;
                    decimals += afterPoint ? 1 : 0;
                    break;
                case DECIMAL_POINT:
                    afterPoint = true;
                    break;
                case PERCENT:
                    percents++;
                    break;
                case 'E' or 'e' or '/':
                    return false;
            }
        }

        thousandsScaling = CommasAfter(section, lastPlaceholder);
        return lastPlaceholder >= 0;
    }

    /// <summary>A comma scales by a thousand only right after the last digit placeholder; one between placeholders groups thousands.</summary>
    private static int CommasAfter(string section, int lastPlaceholder)
    {
        int commas = 0;
        for (int i = lastPlaceholder + 1; lastPlaceholder >= 0 && i < section.Length && section[i] == THOUSANDS; i++)
        {
            commas++;
        }

        return commas;
    }

    private static decimal Power(int number, int exponent)
    {
        decimal result = 1;
        for (int i = 0; i < exponent; i++)
        {
            result *= number;
        }

        return result;
    }

    /// <summary>
    /// Excel's General format in a cell of standard width: 11 characters, the minus sign aside. A number
    /// that fits in fixed notation keeps as many digits as fit, and any other takes scientific notation
    /// with up to five decimals.
    /// </summary>
    private static string General(double value, CultureInfo culture)
    {
        if (value == 0 || !double.IsFinite(value))
        {
            return value == 0 ? "0" : value.ToString(culture);
        }

        string sign = value < 0 ? "-" : string.Empty;
        double magnitude = Math.Abs(value);
        int exponent = (int)Math.Floor(Math.Log10(magnitude));
        string text = exponent switch
        {
            >= GENERAL_FIRST_SHORT_EXPONENT and <= -1 => SignificantFixed((decimal)magnitude, GENERAL_SIGNIFICANT_DIGITS + exponent, exponent),
            >= 0 and <= GENERAL_LAST_FIXED_EXPONENT => FixedOrSignificant((decimal)magnitude, exponent),
            >= -GENERAL_LAST_FIXED_EXPONENT and < GENERAL_FIRST_SHORT_EXPONENT => FixedOrScientific((decimal)magnitude, magnitude),
            GENERAL_ELEVEN_DIGIT_EXPONENT => TrimZeros(Math.Round((decimal)magnitude, 0, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture)),
            _ => Scientific(magnitude),
        };

        return sign + text.Replace(DECIMAL_POINT.ToString(), culture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal);
    }

    private static string FixedOrSignificant(decimal magnitude, int exponent)
    {
        string fixedText = FixedDecimals(magnitude, GENERAL_FIXED_DECIMALS);
        if (fixedText.Length <= GENERAL_WIDTH)
        {
            return fixedText;
        }

        string significant = SignificantFixed(magnitude, GENERAL_SIGNIFICANT_DIGITS, exponent);
        return significant.Length <= GENERAL_WIDTH ? significant : Scientific((double)magnitude);
    }

    private static string FixedOrScientific(decimal magnitude, double value)
    {
        string fixedText = FixedDecimals(magnitude, GENERAL_FIXED_DECIMALS);
        return fixedText.Length <= GENERAL_WIDTH && fixedText != "0" ? fixedText : Scientific(value);
    }

    private static string FixedDecimals(decimal magnitude, int decimals)
    {
        return TrimZeros(Math.Round(magnitude, decimals, MidpointRounding.AwayFromZero).ToString("F" + decimals, CultureInfo.InvariantCulture));
    }

    private static string SignificantFixed(decimal magnitude, int significantDigits, int exponent)
    {
        int decimals = Math.Max(0, significantDigits - 1 - exponent);
        return FixedDecimals(magnitude, decimals);
    }

    private static string Scientific(double magnitude)
    {
        int exponent = (int)Math.Floor(Math.Log10(magnitude));
        decimal mantissa = Math.Round((decimal)(magnitude / Math.Pow(10, exponent)), GENERAL_MANTISSA_DECIMALS, MidpointRounding.AwayFromZero);
        if (mantissa >= 10)
        {
            mantissa /= 10;
            exponent++;
        }

        string digits = TrimZeros(mantissa.ToString("F" + GENERAL_MANTISSA_DECIMALS, CultureInfo.InvariantCulture));
        return digits + "E" + (exponent < 0 ? "-" : "+") + Math.Abs(exponent).ToString("00", CultureInfo.InvariantCulture);
    }

    private static string TrimZeros(string number)
    {
        return number.Contains(DECIMAL_POINT, StringComparison.Ordinal) ? number.TrimEnd('0').TrimEnd(DECIMAL_POINT) : number;
    }

    [GeneratedRegex(@"s\.(?<digits>0+)", RegexOptions.IgnoreCase)]
    private static partial Regex SecondFractionRegex();

    [GeneratedRegex(@"\d[.,](?<digits>\d{3})(?!\d)")]
    private static partial Regex WrittenFractionRegex();
}

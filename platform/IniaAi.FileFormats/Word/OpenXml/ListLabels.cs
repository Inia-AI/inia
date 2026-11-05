using DocumentFormat.OpenXml.Wordprocessing;
using System.Globalization;
using System.Text;

namespace IniaAi.FileFormats.Word.OpenXml;

/// <summary>Writes a list counter in a Word numbering format: decimal, letters, roman numerals or an ordinal.</summary>
internal static class ListLabels
{
    public static string Format(int value, NumberFormatValues? format)
    {
        if (format is null || format == NumberFormatValues.Decimal)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        if (format == NumberFormatValues.DecimalZero)
        {
            return value.ToString("00", CultureInfo.InvariantCulture);
        }

        if (format == NumberFormatValues.LowerLetter || format == NumberFormatValues.UpperLetter)
        {
            string letter = Letters(value);
            return format == NumberFormatValues.UpperLetter ? letter.ToUpperInvariant() : letter;
        }

        if (format == NumberFormatValues.LowerRoman || format == NumberFormatValues.UpperRoman)
        {
            string roman = Roman(value);
            return format == NumberFormatValues.LowerRoman ? roman.ToLowerInvariant() : roman;
        }

        if (format == NumberFormatValues.Ordinal)
        {
            return value + Suffix(value);
        }

        if (format == NumberFormatValues.None)
        {
            return string.Empty;
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    // Word repeats the letter after z: aa, bb, cc.
    private static string Letters(int value)
    {
        if (value <= 0)
        {
            return string.Empty;
        }

        char letter = (char)('a' + ((value - 1) % 26));
        return new string(letter, ((value - 1) / 26) + 1);
    }

    private static string Roman(int value)
    {
        if (value <= 0 || value >= 4000)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        (int Value, string Symbol)[] numerals =
        [
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
            (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        ];
        var builder = new StringBuilder();
        foreach ((int amount, string? symbol) in numerals)
        {
            while (value >= amount)
            {
                _ = builder.Append(symbol);
                value -= amount;
            }
        }

        return builder.ToString();
    }

    private static string Suffix(int value)
    {
        return (value % 100) is 11 or 12 or 13 ? "th" : (value % 10) switch
        {
            1 => "st",
            2 => "nd",
            3 => "rd",
            _ => "th",
        };
    }
}

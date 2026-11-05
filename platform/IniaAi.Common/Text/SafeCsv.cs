using CsvHelper;
using CsvHelper.Configuration;
using System.Globalization;

namespace IniaAi.Common.Text;

/// <summary>
/// The CSV every export writes. A field that starts with =, +, -, @, a tab or a carriage return is
/// escaped with a leading apostrophe, so a spreadsheet shows it as text and never runs it as a
/// formula; CsvHelper writes such a field raw unless told otherwise. A negative number is left as
/// it is, so a spreadsheet still reads it as a number.
/// </summary>
public static class SafeCsv
{
    public static CsvWriter CreateWriter(TextWriter writer, CultureInfo culture, string? delimiter = null)
    {
        return new NumberKeepingCsvWriter(writer, Configuration(culture, delimiter));
    }

    private static CsvConfiguration Configuration(CultureInfo culture, string? delimiter = null)
    {
        var configuration = new CsvConfiguration(culture) { InjectionOptions = InjectionOptions.Escape };

        if (delimiter is not null)
        {
            configuration.Delimiter = delimiter;
        }

        return configuration;
    }

    private sealed class NumberKeepingCsvWriter(TextWriter writer, CsvConfiguration configuration)
        : CsvWriter(writer, configuration)
    {
        private const NumberStyles PLAIN_NUMBER = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent;

        private readonly CultureInfo _culture = configuration.CultureInfo;

        protected override string? SanitizeForInjection(string? field)
        {
            return double.TryParse(field, PLAIN_NUMBER, _culture, out _)
                ? field
                : base.SanitizeForInjection(field);
        }
    }
}

using IniaAi.Core.Workflows.Annotations.Dates;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// Whether a text states the same calendar date as a value written in another date form.
/// A research provider writes a date in prose ("May 2011") while the excerpt it cites keeps
/// the source's own form ("2011-05-01"); the literal tier cannot relate those, and the
/// quantity tier never will, because a date's parts are not quantities. A month-year value
/// is stated by any date in that month. A full date is stated only by a full date on the same
/// day, never by a month-year mention: a source that names only the month does not ground a
/// claim to a specific day. An all-numeric date whose ordering is ambiguous keeps both
/// readings, as <see cref="DateNormalization"/> reads it, so either reading grounds.
/// </summary>
public static class DateEquivalence
{
    /// <summary>Whether the whole value reads as one calendar date.</summary>
    public static bool IsDate(string? value)
    {
        return ReadDate(value) is not null;
    }

    /// <summary>
    /// Whether <paramref name="text"/> states <paramref name="value"/> as a date. The value
    /// is read before the text is scanned, so a value that is no date costs no extraction:
    /// most facts that reach this predicate are quantities, not dates.
    /// </summary>
    public static bool TextStatesDate(string? text, string? value)
    {
        if (string.IsNullOrWhiteSpace(text) || ReadDate(value) is not NormalizedDate claimed)
        {
            return false;
        }

        return StatedBy(NumericalDataExtractor.ExtractDates(text), claimed);
    }

    /// <summary>
    /// Whether any of <paramref name="statedDates"/> — date spans already extracted from a
    /// text — states <paramref name="value"/>. Lets a caller extract a long text once and
    /// test many values against it.
    /// </summary>
    public static bool StatedBy(IEnumerable<string> statedDates, string? value)
    {
        return ReadDate(value) is NormalizedDate claimed
            && StatedBy(statedDates, claimed);
    }

    /// <summary>Whether one whole value, read as a date, states another whole value.</summary>
    public static bool ValueStatesDate(string? stated, string? claimed)
    {
        return !string.IsNullOrWhiteSpace(stated)
            && StatedBy([stated], claimed);
    }

    /// <summary>
    /// Whether two values name the same calendar date in any two forms. Symmetric: a
    /// month-year and a day inside that month are the same date for relatedness, whichever
    /// side is the claim.
    /// </summary>
    public static bool AreSameDate(string? first, string? second)
    {
        return ValueStatesDate(first, second) || ValueStatesDate(second, first);
    }

    private static NormalizedDate? ReadDate(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : DateNormalization.Read(value).Date;
    }

    private static bool StatedBy(IEnumerable<string> statedDates, NormalizedDate claimed)
    {
        foreach (string stated in statedDates)
        {
            if (DateNormalization.Read(stated).Date is NormalizedDate statedDate
                && States(statedDate, claimed))
            {
                return true;
            }
        }

        return false;
    }

    private static bool States(NormalizedDate stated, NormalizedDate claimed)
    {
        if (claimed.Precision == DatePrecision.Day
            && stated.Precision != DatePrecision.Day)
        {
            return false;
        }

        foreach (DateOnly claimedReading in claimed.Readings)
        {
            foreach (DateOnly statedReading in stated.Readings)
            {
                bool sameReading = claimed.Precision == DatePrecision.Month
                    ? statedReading.Year == claimedReading.Year
                        && statedReading.Month == claimedReading.Month
                    : statedReading == claimedReading;

                if (sameReading)
                {
                    return true;
                }
            }
        }

        return false;
    }
}

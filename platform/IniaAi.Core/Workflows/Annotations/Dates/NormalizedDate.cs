namespace IniaAi.Core.Workflows.Annotations.Dates;

/// <summary>
/// A date as a document stated it, carrying the precision actually written and — for an
/// all-numeric value whose ordering is genuinely ambiguous — both readings. An alternate
/// reading is null whenever only one ordering is valid, so equality still means the two sides
/// stated the same thing.
/// </summary>
public sealed record NormalizedDate(
    DatePrecision Precision,
    DateOnly Reading,
    DateOnly? AlternateReading)
{
    public IEnumerable<DateOnly> Readings
    {
        get
        {
            yield return Reading;

            if (AlternateReading is DateOnly alternate)
            {
                yield return alternate;
            }
        }
    }
}

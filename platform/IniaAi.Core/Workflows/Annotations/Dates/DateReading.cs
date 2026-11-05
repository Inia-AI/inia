namespace IniaAi.Core.Workflows.Annotations.Dates;

public readonly record struct DateReading(
    DateReadOutcome Outcome,
    NormalizedDate? Date);

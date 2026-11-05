namespace IniaAi.Domain.Entities.ReportFeedback;

/// <summary>
/// What a reader says is wrong with a report. Stored as its ordinal: append new
/// members before the closing brace, never insert.
/// </summary>
public enum ReportFeedbackCategory
{
    WrongFigure = 0,
    MissingInformation = 1,
    WrongClassification = 2,
    WordingOrStyle = 3,
    NotRelevant = 4,
}

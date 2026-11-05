namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// What a reader says is wrong with a report. Mirrors the domain
/// <c>ReportFeedbackCategory</c>; serialized by name on the wire.
/// </summary>
public enum ReportFeedbackCategoryDto
{
    /// <summary>
    /// A number in the output is wrong.
    /// </summary>
    WrongFigure = 0,

    /// <summary>
    /// Something the reader expected is absent from the output.
    /// </summary>
    MissingInformation = 1,

    /// <summary>
    /// An item was put in the wrong class, bucket or line.
    /// </summary>
    WrongClassification = 2,

    /// <summary>
    /// The content is right but the wording or presentation is not.
    /// </summary>
    WordingOrStyle = 3,

    /// <summary>
    /// The output does not belong in this report.
    /// </summary>
    NotRelevant = 4,
}

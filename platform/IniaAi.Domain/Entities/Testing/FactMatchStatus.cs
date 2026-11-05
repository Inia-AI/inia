namespace IniaAi.Domain.Entities.Testing;

public enum FactMatchStatus
{
    Match = 0,
    ValueDiffers = 1,
    MissingInRerun = 2,
    NewInRerun = 3,
    WithinTolerance = 4,

    /// <summary>
    /// The field declared an extraction source but its value could not be located (or, for a numeric
    /// field, could not be parsed) despite the block producing output. Surfaced distinctly: it blocks a
    /// clean pass but is not a value difference and is excluded from the similarity score.
    /// </summary>
    ExtractionFailed = 5
}

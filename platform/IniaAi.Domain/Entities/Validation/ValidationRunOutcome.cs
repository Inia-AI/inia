namespace IniaAi.Domain.Entities.Validation;

public enum ValidationRunOutcome
{
    Pass = 0,
    Fail = 1,
    NeedsReview = 2,
    NothingToScore = 3,
    Warning = 4,
}

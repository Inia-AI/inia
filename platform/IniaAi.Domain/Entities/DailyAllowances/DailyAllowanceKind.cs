namespace IniaAi.Domain.Entities.DailyAllowances;

/// <summary>
/// A kind of request a user may make a limited number of times a UTC day. Stored as an int: append
/// a new kind, never insert one.
/// </summary>
public enum DailyAllowanceKind
{
    Explore = 0,
    Refine = 1,
    HelpRequest = 2,
    HelpRequestReply = 3,
}

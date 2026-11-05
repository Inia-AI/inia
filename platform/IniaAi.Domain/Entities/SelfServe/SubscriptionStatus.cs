namespace IniaAi.Domain.Entities.SelfServe;

/// <summary>
/// Active, past due and trialing keep running reports; canceled and unpaid keep them only while
/// credits already paid for last; incomplete, incomplete expired and paused leave the organization
/// able to read and delete only.
/// </summary>
public enum SubscriptionStatus
{
    Incomplete,
    Active,
    PastDue,
    Canceled,
    Unpaid,
    IncompleteExpired,
    Trialing,
    Paused,
}

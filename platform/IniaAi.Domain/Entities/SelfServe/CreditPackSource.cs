namespace IniaAi.Domain.Entities.SelfServe;

public enum CreditPackSource
{
    /// <summary>A paid month of the monthly plan, sold until 12 November 2026.</summary>
    Subscription,
    Operator,
    TopUp,
}

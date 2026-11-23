namespace IniaAi.Domain.Entities.SelfServe;

public enum CreditPackSource
{
    /// <summary>A paid month of the monthly plan, sold until 12 November 2026.</summary>
    Subscription,
    Operator,
    TopUp,

    /// <summary>The free credit a new account gets at sign-up; not a payment.</summary>
    FreeCredit,
}

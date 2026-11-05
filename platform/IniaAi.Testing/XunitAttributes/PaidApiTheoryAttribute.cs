using Xunit;

namespace IniaAi.Testing.XunitAttributes;

/// <summary>
/// A theory that calls a paid API. It runs only when <see cref="PaidApi.OPT_IN_VARIABLE"/> is set,
/// which CI does only in its nightly run, and in CI only when <paramref name="alsoInCi"/> is true.
/// </summary>
public sealed class PaidApiTheoryAttribute : TheoryAttribute
{
    public PaidApiTheoryAttribute(string reason, bool alsoInCi = false)
    {
        Skip = PaidApi.SkipReason(reason, alsoInCi);
    }
}

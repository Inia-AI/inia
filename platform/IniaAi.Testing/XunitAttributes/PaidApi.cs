namespace IniaAi.Testing.XunitAttributes;

/// <summary>
/// Keeps test runs from spending money. A test that calls a paid API (a model, embeddings, web
/// search) runs only when <see cref="OPT_IN_VARIABLE"/> is set: locally on request, and in CI only in
/// the nightly run. Without it, the test connector factories hand out connectors that refuse every
/// call, and the test configuration replaces every stored key that is not known to be free with
/// <see cref="WITHHELD_KEY"/>.
/// </summary>
public static class PaidApi
{
    public const string OPT_IN_VARIABLE = "INIA_TEST_PAID_APIS";

    public const string WITHHELD_KEY = $"{OPT_IN_VARIABLE}-not-set";

    private const string OPTED_IN = "1";

    public static bool IsAllowed()
    {
        return Environment.GetEnvironmentVariable(OPT_IN_VARIABLE) == OPTED_IN;
    }

    internal static string? SkipReason(string reason, bool alsoInCi)
    {
        return SkipReason(reason, alsoInCi, Utils.IsRunningOnCI(), IsAllowed());
    }

    internal static string? SkipReason(string reason, bool alsoInCi, bool onCi, bool optedIn)
    {
        if (!optedIn)
        {
            return $"Calls a paid API ({reason}). Set {OPT_IN_VARIABLE}=1 to run it.";
        }

        return onCi && !alsoInCi ? $"Ignored in CI: {reason}" : null;
    }
}

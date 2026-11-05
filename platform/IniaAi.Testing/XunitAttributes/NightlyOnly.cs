namespace IniaAi.Testing.XunitAttributes;

/// <summary>
/// Moves a slow test out of the pull-request run. On CI it runs only when <see cref="NIGHTLY_VARIABLE"/>
/// is set, which the nightly run does; off CI it always runs.
/// </summary>
public static class NightlyOnly
{
    public const string NIGHTLY_VARIABLE = "INIA_TEST_NIGHTLY";

    private const string ON = "1";

    internal static string? SkipReason(string reason)
    {
        return SkipReason(reason, Utils.IsRunningOnCI(), Environment.GetEnvironmentVariable(NIGHTLY_VARIABLE) == ON);
    }

    internal static string? SkipReason(string reason, bool onCi, bool isNightly)
    {
        return onCi && !isNightly ? $"Runs in the nightly CI run: {reason}" : null;
    }
}

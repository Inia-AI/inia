namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// One deployment's standing for one workflow, relative to the benchmark — the most recently saved
/// version of that workflow across all compared deployments. Derived by comparing version-hash
/// lineages, so a deployment that is simply behind is told apart from one that has diverged.
/// </summary>
public enum CurrencyCellStatus
{
    /// <summary>This deployment holds the same version as the benchmark.</summary>
    Match,

    /// <summary>This deployment holds an earlier point in the benchmark's own history.</summary>
    Behind,

    /// <summary>This deployment carries later edits than the benchmark, on the same line.</summary>
    Ahead,

    /// <summary>This deployment was edited on a separate line from the benchmark — neither is simply newer.</summary>
    Diverged,

    /// <summary>This deployment has no saved version of the workflow.</summary>
    Missing,
}

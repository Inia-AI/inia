namespace IniaAi.Testing;

/// <summary>What one measured answer showed about a planted instruction.</summary>
public enum MarkerOutcome
{
    MarkerAppeared,
    Clean,

    /// <summary>The call errored, the model refused, or nothing came back: never counted as clean.</summary>
    Inconclusive,
}

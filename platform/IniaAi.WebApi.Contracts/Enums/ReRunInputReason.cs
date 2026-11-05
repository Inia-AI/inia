namespace IniaAi.WebApi.Contracts.Enums;

/// <summary>
/// Why a variable needs input from the user before a re-run.
/// </summary>
public enum ReRunInputReason
{
    /// <summary>
    /// The run had no value for the variable and the variable has no default. Typically a
    /// variable the workflow gained after the run.
    /// </summary>
    NoValue,

    /// <summary>
    /// The run's value no longer passes the variable's rules, for example a dropdown option that
    /// was removed.
    /// </summary>
    InvalidValue,

    /// <summary>
    /// The variable is confidential. Its value is never returned, so the user enters it again.
    /// </summary>
    Confidential,
}

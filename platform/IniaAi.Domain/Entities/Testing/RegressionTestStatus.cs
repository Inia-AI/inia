namespace IniaAi.Domain.Entities.Testing;

/// <summary>
/// Status of a regression test execution.
/// </summary>
public enum RegressionTestStatus
{
    /// <summary>
    /// Status not specified or unknown.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Test passed successfully.
    /// </summary>
    Success = 1,

    /// <summary>
    /// Test passed but with warnings.
    /// </summary>
    WithWarnings = 2,

    /// <summary>
    /// Test failed.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Test execution encountered errors.
    /// </summary>
    Error = 4
}

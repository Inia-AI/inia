namespace IniaAi.Application.Common.Interfaces.Persistence;

/// <summary>
/// Analyzes database exceptions to determine the type of constraint violation.
/// </summary>
public interface IDbExceptionAnalyzer
{
    /// <summary>
    /// Determines if the exception represents a counterparty name duplication constraint violation.
    /// </summary>
    /// <param name="exception">The exception to analyze.</param>
    /// <returns>True if this is a counterparty name duplication error.</returns>
    bool IsCounterpartyNameDuplicate(Exception exception);

    /// <summary>
    /// Determines if the exception represents any unique constraint violation.
    /// </summary>
    /// <param name="exception">The exception to analyze.</param>
    /// <returns>True if this is a unique constraint violation.</returns>
    bool IsUniqueConstraintViolation(Exception exception);

    /// <summary>
    /// Determines if the exception represents an optimistic concurrency conflict,
    /// i.e. another request modified or removed the same rows first.
    /// </summary>
    /// <param name="exception">The exception to analyze.</param>
    /// <returns>True if this is a concurrency conflict.</returns>
    bool IsConcurrencyConflict(Exception exception);
}

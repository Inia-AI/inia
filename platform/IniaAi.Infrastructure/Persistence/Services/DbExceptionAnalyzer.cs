using IniaAi.Application.Common.Interfaces.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace IniaAi.Infrastructure.Persistence.Services;

/// <summary>
/// Analyzes database exceptions to determine the type of constraint violation.
/// </summary>
public class DbExceptionAnalyzer : IDbExceptionAnalyzer
{
    /// <inheritdoc/>
    public bool IsCounterpartyNameDuplicate(Exception exception)
    {
        return exception is DbUpdateException dbEx && dbEx.InnerException switch
        {
            PostgresException pgEx => pgEx.SqlState is PostgresErrorCodes.UniqueViolation &&
                pgEx.ConstraintName == "IX_Counterparties_Name_OrganizationId_NotDeleted",
            SqlException sqlEx => sqlEx.Number is 2601 or 2627 &&
                sqlEx.Message.Contains("IX_Counterparties_Name_OrganizationId_NotDeleted"),
            _ => false
        };
    }

    /// <inheritdoc/>
    public bool IsUniqueConstraintViolation(Exception exception)
    {
        return exception is DbUpdateException dbEx && dbEx.InnerException switch
        {
            PostgresException pgEx => pgEx.SqlState is PostgresErrorCodes.UniqueViolation,
            SqlException sqlEx => sqlEx.Number is 2601 or 2627,
            _ => false
        };
    }

    /// <inheritdoc/>
    public bool IsConcurrencyConflict(Exception exception)
    {
        return exception is DbUpdateConcurrencyException;
    }
}

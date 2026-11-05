using IniaAi.Domain.Entities.Workflows.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Persistence.Setup;

/// <summary>
/// Hard-deletes RagSettings whose <see cref="RagTypes"/> value no longer exists in the enum,
/// so removing a member from <see cref="RagTypes"/> prunes its persisted settings on the next
/// startup. The supported set is read live from the enum — it cannot drift from a hand-list.
/// Called from <see cref="DatabaseMigrationHelper.MigrateDatabase"/> after schema migrations.
/// </summary>
public static class ObsoleteRagSettingsCleanupHelper
{
    private static readonly int[] _supportedRagTypes =
        [.. Enum.GetValues<RagTypes>().Select(type => (int)type).Distinct()];

    public static int DeleteObsoleteRagSettings(DbContext context, ILogger logger)
    {
        string contextType = context.GetType().Name.Replace("DbContext", string.Empty);
        string schema = contextType;
        string q = GetQuoteChar(context);
        string closingQ = q == "[" ? "]" : q;
        string supportedTypes = string.Join(", ", _supportedRagTypes);

        IExecutionStrategy strategy = context.Database.CreateExecutionStrategy();

        return strategy.Execute(() =>
        {
            using IDbContextTransaction transaction = context.Database.BeginTransaction();

            try
            {
                // No table holds a foreign key to RagSettings, so the rows delete directly —
                // no referencer-clearing step is needed.
                string deleteRagSettings = $"""
                    DELETE FROM {q}{schema}{closingQ}.{q}RagSettings{closingQ}
                    WHERE {q}Type{closingQ} NOT IN ({supportedTypes})
                    """;
                int deleted = context.Database.ExecuteSqlRaw(deleteRagSettings);

                transaction.Commit();

                logger.LogInformation(
                    "{Context}: Obsolete RAG-settings cleanup removed {Count} row(s)",
                    contextType, deleted);

                return deleted;
            }
            catch (Exception ex)
            {
                // No explicit Rollback: on a broken connection it would throw and replace the
                // original exception before the execution strategy can classify it; disposing
                // the uncommitted transaction rolls back defensively instead.
                logger.LogError(
                    ex,
                    "{Context}: Obsolete RAG-settings cleanup failed — rolled back, no changes persisted",
                    contextType);
                throw;
            }
        });
    }

    private static string GetQuoteChar(DbContext context)
    {
        return context.Database.ProviderName?.Contains("Npgsql") == true ? "\"" : "[";
    }
}

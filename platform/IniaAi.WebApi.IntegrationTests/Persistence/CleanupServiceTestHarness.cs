using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Services;
using IniaAi.Configuration.Storage;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using IniaAi.Infrastructure.Services;
using IniaAi.Testing.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// Shared wiring for cleanup-service tests over <see cref="PostgresUserDataDatabaseFixture"/>:
/// one owner for the service's constructor dependencies, so a signature change breaks one file.
/// A test that exercises a purge step passes it in; the platform tests pass none.
/// </summary>
internal static class CleanupServiceTestHarness
{
    public const string SKIP_REASON = "No local PostgreSQL reachable (set INIA_TEST_PG to enable).";

    public static SoftDeleteCleanupService CreateService(UserDataDbContext context, params IDocumentPurgeStep[] documentPurgeSteps)
    {
        var fileStorage = new FileStorageManager(
            new TestFileStorageResolver(new TestFileStorageService()),
            Options.Create(new FileStorageSettings { DefaultProvider = "Database" }));

        return new SoftDeleteCleanupService(context, fileStorage, documentPurgeSteps, NullLogger<SoftDeleteCleanupService>.Instance);
    }
}

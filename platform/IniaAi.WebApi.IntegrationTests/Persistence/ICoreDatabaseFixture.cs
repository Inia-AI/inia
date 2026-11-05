using IniaAi.Infrastructure.Persistence.Core.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace IniaAi.WebApi.IntegrationTests.Persistence;

/// <summary>
/// A throwaway Core-schema database on a real provider, for tests that need behavior the InMemory
/// provider cannot express (real transactions, enforced unique indexes, provider exceptions).
/// </summary>
public interface ICoreDatabaseFixture
{
    bool DatabaseAvailable { get; }

    DbContextOptions<CoreDbContext> Options { get; }

    DbContextOptions<CoreDbContext> BuildOptions(params IInterceptor[] interceptors);

    DbContextOptions<CoreDbContext> BuildRetryingOptions(params IInterceptor[] interceptors);

    CoreDbContext CreateContext();
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace IniaAi.WebApi.HealthChecks;
/// <summary>
/// Health check that verifies database connectivity using a <see cref="IDbContextFactory{TContext}"/>.
/// </summary>
/// <typeparam name="TContext">The DbContext type.</typeparam>
public class DbContextFactoryHealthCheck<TContext> : IHealthCheck where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _dbContextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DbContextFactoryHealthCheck{TContext}"/> class.
    /// </summary>
    /// <param name="dbContextFactory">The database context factory.</param>
    public DbContextFactoryHealthCheck(IDbContextFactory<TContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            // Create a DbContext instance using the factory
            await using TContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
            _ = await dbContext.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);

            return HealthCheckResult.Healthy("Database is reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database is unreachable", ex);
        }
    }
}

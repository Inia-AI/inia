namespace IniaAi.Common.Interfaces.Persistence;

/// <summary>
/// A one-time data change that startup runs under the database lock, after the schema migrations
/// and the seeding. It finds its own remaining work, so it does nothing once that work is done,
/// and an exception stops startup the way a failed schema migration does.
/// </summary>
public interface IStartupDataMigration
{
    Task RunAsync(CancellationToken cancellationToken);
}

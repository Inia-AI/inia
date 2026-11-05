using IniaAi.Application.Common.Interfaces.Persistence;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Writes anchored trace rows through a short-lived <see cref="UserDataDbContext"/> of its own —
/// a fresh scope, because the executor calls it from teardown paths whose request scope may
/// already be dying, and because block persistence must never share a change tracker with it.
/// Idempotent by the row's natural key (run execution id, attempt, path): a replayed teardown or
/// a cleanup re-collection after a crash re-commits nothing, however the row ids differ between
/// collections. Concurrent persists of the same run (a watcher's cancellation teardown racing
/// terminal cleanup) are converged rather than duplicated: the unique natural-key index makes
/// the losing insert throw a provider-typed unique violation, and one retry against the
/// winner's committed state finds every row already present. Only a genuine unique violation
/// earns that retry — any other save failure (a foreign key on a concurrently deleted block,
/// a dying connection) is not a race and cannot converge, so it fails straight to false. One
/// SaveChanges commits all rows atomically; any failure answers false so the caller withholds
/// the run's acknowledgement and the runtime keeps the pack.
/// </summary>
public class AgentRunTracePersister : IAgentRunTracePersister
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IDbExceptionAnalyzer _exceptionAnalyzer;
    private readonly ILogger<AgentRunTracePersister> _logger;
    private readonly Func<int, Task>? _onBeforeSave;

    public AgentRunTracePersister(
        IServiceScopeFactory serviceScopeFactory,
        IDbExceptionAnalyzer exceptionAnalyzer,
        ILogger<AgentRunTracePersister> logger)
        : this(serviceScopeFactory, exceptionAnalyzer, logger, onBeforeSave: null)
    {
    }

    internal AgentRunTracePersister(
        IServiceScopeFactory serviceScopeFactory,
        IDbExceptionAnalyzer exceptionAnalyzer,
        ILogger<AgentRunTracePersister> logger,
        Func<int, Task>? onBeforeSave)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _exceptionAnalyzer = exceptionAnalyzer;
        _logger = logger;
        _onBeforeSave = onBeforeSave;
    }

    public async Task<bool> PersistAsync(
        IReadOnlyList<AgentRunTraceFile> files,
        CancellationToken cancellationToken = default)
    {
        if (files.Count == 0)
        {
            return true;
        }

        try
        {
            await CommitMissingRowsAsync(files, attempt: 1, cancellationToken);

            return true;
        }
        catch (DbUpdateException exception) when (_exceptionAnalyzer.IsUniqueConstraintViolation(exception))
        {
            _logger.LogInformation(
                exception,
                "Persisting trace rows for run(s) {RunExecutionIds} lost an insert race; retrying once against the committed state",
                RunIdsOf(files));

            try
            {
                await CommitMissingRowsAsync(files, attempt: 2, cancellationToken);

                return true;
            }
            catch (Exception retryException)
            {
                LogFailure(retryException, files);

                return false;
            }
        }
        catch (Exception exception)
        {
            LogFailure(exception, files);

            return false;
        }
    }

    private async Task CommitMissingRowsAsync(
        IReadOnlyList<AgentRunTraceFile> files,
        int attempt,
        CancellationToken cancellationToken)
    {
        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        UserDataDbContext context = scope.ServiceProvider.GetRequiredService<UserDataDbContext>();

        foreach (IGrouping<(string RunExecutionId, int Attempt), AgentRunTraceFile> pack in
            files.GroupBy(static file => (file.RunExecutionId, file.Attempt)))
        {
            HashSet<string> alreadyPersisted = (await context.Set<AgentRunTraceFile>()
                .Where(row => row.RunExecutionId == pack.Key.RunExecutionId
                    && row.Attempt == pack.Key.Attempt)
                .Select(row => row.Path)
                .ToListAsync(cancellationToken)).ToHashSet(StringComparer.Ordinal);

            foreach (AgentRunTraceFile file in pack)
            {
                if (!alreadyPersisted.Contains(file.Path))
                {
                    _ = context.Entry(file).State = EntityState.Added;
                }
            }
        }

        if (_onBeforeSave is not null)
        {
            await _onBeforeSave(attempt);
        }

        _ = await context.SaveChangesAsync(cancellationToken);
    }

    private void LogFailure(Exception exception, IReadOnlyList<AgentRunTraceFile> files)
    {
        _logger.LogError(
            exception,
            "Persisting {FileCount} trace row(s) for run(s) {RunExecutionIds} failed; the run's acknowledgement must be withheld",
            files.Count,
            RunIdsOf(files));
    }

    private static string RunIdsOf(IReadOnlyList<AgentRunTraceFile> files)
    {
        return string.Join(", ", files.Select(static file => file.RunExecutionId).Distinct());
    }
}

using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Queues;

/// <summary>
/// The attempt discipline every queued runner shares. The entity is loaded tracked
/// and the work skipped when it no longer exists or has already left the state it was queued in
/// (a duplicate or superseded request). A commit that loses to a newer state write — the entity's
/// concurrency token moved under a restart, a resubmission, an archive — is discarded without a
/// trace, because the entity has moved on. Any other failure is recorded on a fresh read of the
/// entity, and only while that read still belongs to this attempt — the token unchanged — so a
/// failure never lands on a newer attempt's state. A stopping host is recorded the same way, in
/// its own words: an interrupted attempt and a request drained before it started both leave the
/// entity saying so, ready for its retry at once rather than after a deadline. Work that
/// <see cref="ResumesOnNextStart"/> is left as it stands instead, and only the log says so.
/// </summary>
public abstract class WorkRunner<TRequest, TEntity> : IWorkRunner<TRequest>
    where TEntity : class
{
    protected WorkRunner(ILogger logger, IUserDataUnitOfWork userData)
    {
        Logger = logger;
        UserData = userData;
    }

    protected ILogger Logger { get; }

    protected IUserDataUnitOfWork UserData { get; }

    public async Task RunAsync(TRequest request, CancellationToken cancellationToken)
    {
        TEntity? entity = await LoadAsync(request, cancellationToken);

        if (entity is null)
        {
            Logger.LogWarning("{Work} was queued but no longer exists", Describe(request));
            return;
        }

        if (!IsQueued(entity))
        {
            Logger.LogWarning("{Work} was queued but is {State}; nothing to do", Describe(request), StateOf(entity));
            return;
        }

        try
        {
            await ExecuteAsync(entity, request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            UserData.ClearChangeTracker();

            if (ResumesOnNextStart)
            {
                Logger.LogInformation("{Work}: {Message}", Describe(request), InterruptedMessage);
            }
            else
            {
                await RecordFailureAsync(entity, request, InterruptedMessage);
            }

            throw;
        }
        catch (DbUpdateConcurrencyException)
        {
            UserData.ClearChangeTracker();
            Logger.LogWarning("{Work} lost to a newer write; its result was discarded", Describe(request));
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "{Work} failed", Describe(request));
            UserData.ClearChangeTracker();
            await RecordFailureAsync(entity, request, exception.Message);
        }
    }

    public async Task RecordNotStartedAsync(TRequest request)
    {
        if (ResumesOnNextStart)
        {
            Logger.LogInformation("{Work}: {Message}", Describe(request), NotStartedMessage);
            return;
        }

        TEntity? entity = await LoadAsync(request, CancellationToken.None);

        if (entity is null || !IsQueued(entity))
        {
            return;
        }

        RecordFailure(entity, NotStartedMessage);
        await CommitFailureAsync(request);
    }

    /// <summary>
    /// What the entity says when a stopping host cut its attempt short; for work that
    /// <see cref="ResumesOnNextStart"/>, what the log says.
    /// </summary>
    protected abstract string InterruptedMessage { get; }

    /// <summary>
    /// What the entity says when a stopping host drained its request before it ran; for work that
    /// <see cref="ResumesOnNextStart"/>, what the log says.
    /// </summary>
    protected abstract string NotStartedMessage { get; }

    /// <summary>
    /// Whether a stopping host leaves the entity as it stands, for the next start to take the work
    /// up again where it stopped, instead of recording on it that the host cut it short.
    /// </summary>
    protected virtual bool ResumesOnNextStart => false;

    /// <summary>
    /// Loads the entity tracked, so the work's writes ride the unit of work.
    /// </summary>
    protected abstract Task<TEntity?> LoadAsync(TRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Whether the entity still awaits this work — the state the request was queued in; a
    /// duplicate or superseded request finds it moved on and does nothing.
    /// </summary>
    protected abstract bool IsQueued(TEntity entity);

    /// <summary>
    /// Whether a fresh read of the entity still belongs to the attempt that loaded it — its
    /// concurrency token unchanged — so a failure may be written on it.
    /// </summary>
    protected abstract bool IsSameAttempt(TEntity loaded, TEntity current);

    /// <summary>
    /// The entity's state as a word for the log line written when the work is skipped.
    /// </summary>
    protected abstract string StateOf(TEntity entity);

    /// <summary>
    /// The work itself; every commit inside it is the implementation's own.
    /// </summary>
    protected abstract Task ExecuteAsync(TEntity entity, TRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the failure onto the entity; the base commits it.
    /// </summary>
    protected abstract void RecordFailure(TEntity entity, string message);

    /// <summary>
    /// Names the work for its log lines.
    /// </summary>
    protected abstract string Describe(TRequest request);

    private async Task RecordFailureAsync(TEntity loaded, TRequest request, string message)
    {
        TEntity? current = await LoadAsync(request, CancellationToken.None);

        if (current is null || !IsSameAttempt(loaded, current))
        {
            Logger.LogWarning("{Work} moved on before its failure could be recorded; the failure was dropped", Describe(request));
            return;
        }

        RecordFailure(current, message);
        await CommitFailureAsync(request);
    }

    private async Task CommitFailureAsync(TRequest request)
    {
        try
        {
            _ = await UserData.CompleteAsync(CancellationToken.None);
        }
        catch (DbUpdateConcurrencyException)
        {
            UserData.ClearChangeTracker();
            Logger.LogWarning("{Work} moved on before its failure could be recorded; the failure was dropped", Describe(request));
        }
    }
}

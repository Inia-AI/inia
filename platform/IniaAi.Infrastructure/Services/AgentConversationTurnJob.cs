using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Domain.Entities.AgentRuntime;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The body of one dispatched conversation turn, run inside its job's scope: the purpose's
/// consumer stages what the run reads, the shared runner runs and settles it. Whatever goes wrong
/// on the way, the turn settles: Cancelled when the job was cancelled, Failed otherwise, so a turn
/// is never left in flight by a fault. When the turn has settled, the next waiting turns start.
/// </summary>
public sealed class AgentConversationTurnJob
{
    private readonly IServiceProvider _services;
    private readonly AgentConversationLedger _ledger;
    private readonly AgentConversationTurns _turns;
    private readonly ILogger<AgentConversationTurnJob> _logger;
    private readonly IAgentConversationRunner? _runner;

    public AgentConversationTurnJob(
        IServiceProvider services,
        AgentConversationLedger ledger,
        AgentConversationTurns turns,
        ILogger<AgentConversationTurnJob> logger,
        IAgentConversationRunner? runner = null)
    {
        _services = services;
        _ledger = ledger;
        _turns = turns;
        _logger = logger;
        _runner = runner;
    }

    public async Task RunAsync(string turnId, CancellationToken cancellationToken)
    {
        string? userId = null;

        try
        {
            AgentConversationTurn turn = await _services.GetRequiredService<UserDataDbContext>()
                .Set<AgentConversationTurn>()
                .AsNoTracking()
                .Include(static row => row.Conversation)
                .SingleAsync(row => row.Id == turnId, cancellationToken);
            AgentConversation conversation = turn.Conversation!;
            userId = turn.UserId;

            if (!turn.Status.IsInFlight())
            {
                return;
            }

            IAgentConversationConsumer consumer = _services.GetServices<IAgentConversationConsumer>()
                .Single(candidate => candidate.Purpose == conversation.Purpose);

            AgentConversationTurnRequest request;
            try
            {
                request = await consumer.StageTurnAsync(turn, conversation, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogError(exception, "Staging conversation turn {TurnId} failed", turnId);
                _ = await _ledger.SettleAsync(
                    turnId,
                    new AgentConversationVerdict(AgentConversationTurnStatus.Failed, null, 0m, AgentConversationTurnErrors.STAGING_FAILED));
                return;
            }

            if (_runner is null)
            {
                _ = await _ledger.SettleAsync(
                    turnId,
                    new AgentConversationVerdict(AgentConversationTurnStatus.Failed, null, 0m, AgentConversationTurnErrors.RUNTIME_OFF));
                return;
            }

            _ = await _runner.RunTurnAsync(request, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SettleQuietlyAsync(turnId, AgentConversationTurnStatus.Cancelled, AgentConversationTurnErrors.CANCELLED);
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Conversation turn {TurnId} failed", turnId);
            await SettleQuietlyAsync(turnId, AgentConversationTurnStatus.Failed, AgentConversationTurnErrors.INTERNAL_FAILURE);
        }
        finally
        {
            await StartNextQuietlyAsync(userId);
        }
    }

    /// <summary>
    /// The last line of defence: the runner settles its own faults with the spend it saw, so a
    /// turn still in flight here never reached a run, and settles at no cost.
    /// </summary>
    private async Task SettleQuietlyAsync(string turnId, AgentConversationTurnStatus status, string error)
    {
        try
        {
            _ = await _ledger.SettleAsync(turnId, new AgentConversationVerdict(status, null, 0m, error));
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Settling conversation turn {TurnId} failed; the overdue sweep settles it", turnId);
        }
    }

    /// <summary>
    /// The settled turn freed its conversation and one of its user's running slots: the
    /// conversation's next waiting turn starts, or another conversation's turn that the cap held.
    /// </summary>
    private async Task StartNextQuietlyAsync(string? userId)
    {
        if (userId is null)
        {
            return;
        }

        try
        {
            _ = await _turns.DispatchReadyAsync(userId, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Starting the next waiting turns of user {UserId} failed; the turn sweep starts them", userId);
        }
    }
}

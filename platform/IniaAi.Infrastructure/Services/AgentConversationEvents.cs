using IniaAi.Common;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// The process-wide feed of conversation turn status changes. A subscriber's fault never reaches
/// the writer that settled the turn.
/// </summary>
public sealed class AgentConversationEvents : IAgentConversationEvents
{
    private const string SUBSCRIBER_FAILED = "A subscriber failed to take a conversation turn change";

    private readonly ILogger<AgentConversationEvents> _logger;

    public AgentConversationEvents(ILogger<AgentConversationEvents> logger)
    {
        _logger = logger;
    }

    public event EventHandler<AgentConversationTurnChanged>? TurnChanged;

    public void Publish(AgentConversationTurnChanged change)
    {
        TurnChanged.RaiseEach(this, change, _logger, SUBSCRIBER_FAILED);
    }
}

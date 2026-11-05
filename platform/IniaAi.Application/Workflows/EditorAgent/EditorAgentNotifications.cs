using IniaAi.Common;
using IniaAi.Common.Interfaces.Services.AgentRuntime;
using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;
using IniaAi.Domain.Entities.AgentRuntime;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Workflows.EditorAgent;

/// <summary>
/// One process-wide feed of turn updates. Status changes arrive from the shared conversation
/// layer's events and the agent's steps from the shared live activity feed; this relays both for
/// the editor agent's conversations only.
/// </summary>
public sealed class EditorAgentNotifications : IEditorAgentNotifications, IDisposable
{
    private const string SUBSCRIBER_FAILED = "An editor agent panel failed to take a turn update";

    private readonly IAgentRunActivityFeed _activityFeed;
    private readonly IAgentConversationEvents _conversationEvents;
    private readonly ILogger<EditorAgentNotifications> _logger;

    public EditorAgentNotifications(
        IAgentRunActivityFeed activityFeed,
        IAgentConversationEvents conversationEvents,
        ILogger<EditorAgentNotifications> logger)
    {
        _activityFeed = activityFeed;
        _conversationEvents = conversationEvents;
        _logger = logger;
        _activityFeed.EntryChanged += OnActivity;
        _conversationEvents.TurnChanged += OnTurnChanged;
    }

    public event EventHandler<EditorAgentTurnUpdate>? TurnUpdated;

    public void Dispose()
    {
        _activityFeed.EntryChanged -= OnActivity;
        _conversationEvents.TurnChanged -= OnTurnChanged;
    }

    private void Publish(EditorAgentTurnUpdate update)
    {
        TurnUpdated.RaiseEach(this, update, _logger, SUBSCRIBER_FAILED);
    }

    private void OnTurnChanged(object? sender, AgentConversationTurnChanged change)
    {
        if (change.Purpose != WorkflowEditorAgentService.PURPOSE)
        {
            return;
        }

        Publish(new EditorAgentTurnUpdate(
            change.OrganizationId, change.ConversationId, change.TurnNumber, change.Status, Activity: null));
    }

    private void OnActivity(object? sender, AgentRunActivityEntry entry)
    {
        if (entry.Registration.Purpose != WorkflowEditorAgentService.PURPOSE
            || entry.Registration.ConversationId is not string conversationId)
        {
            return;
        }

        Publish(new EditorAgentTurnUpdate(
            entry.Registration.OrganizationId,
            conversationId,
            entry.Registration.Round,
            entry.Settled ? null : AgentConversationTurnStatus.Running,
            entry.Digest));
    }
}

using IniaAi.Common.Interfaces.Services.AgentRuntime.Activity;

namespace IniaAi.Application.UnitTests.Queries.WorkflowExecutions.GetWorkflowExecution;

/// <summary>An agent activity feed with no live run, for a report test that does not read agent activity.</summary>
internal sealed class SilentAgentRunActivityFeed : IAgentRunActivityFeed
{
    public void BeginRun(AgentRunActivityRegistration registration)
    {
    }

    public void PublishDigest(string runExecutionId, AgentActivityDigest digest)
    {
    }

    public void ConcludeRun(string runExecutionId)
    {
    }

    public IReadOnlyList<AgentRunActivityEntry> SnapshotForExecution(
        string organizationId,
        string workflowExecutionId) => [];

    public IReadOnlyList<AgentRunActivityEntry> SnapshotForConversation(
        string organizationId,
        string conversationId) => [];

    public event EventHandler<AgentRunActivityEntry>? EntryChanged
    {
        add
        {
        }

        remove
        {
        }
    }
}

using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.WebApp.Components.WorkflowEditor.Agent;

/// <summary>
/// How the editor marks a block or section the agent's accepted changes touched, until the next
/// save, and one the agent proposes to change, until the user decides.
/// </summary>
public static class EditorAgentMarks
{
    public const string CHANGED_CLASS = "editor-agent-changed";
    public const string CHANGED_LABEL = "Changed by the agent";
    public const string PROPOSED_CLASS = "editor-agent-proposed";
    public const string PROPOSED_LABEL = "The agent proposes a change";
}

/// <summary>
/// Accepted changes landed on the editor's workflow, or a proposal's changes were undone.
/// </summary>
/// <param name="ProposalId">The proposal the changes belong to.</param>
/// <param name="ChangedStableIds">The blocks and sections the accepted changes touched, by stable id;
/// for an undo, the ones taken back, or empty when the whole proposal was.</param>
/// <param name="Undone">True when the proposal's accepted changes were taken back.</param>
public sealed record EditorAgentWorkflowEdit(string ProposalId, IReadOnlyList<string> ChangedStableIds, bool Undone);

/// <summary>
/// The workflow as it would be with every change the agent proposed that nobody has decided yet,
/// for the dependency graph to draw beside the workflow as it is.
/// </summary>
/// <param name="Workflow">A copy of the editor's workflow with those changes applied; never saved.</param>
/// <param name="MarksByBlockId">For each block in the copy the changes touch, by row id: <see cref="ADDED"/> or <see cref="CHANGED"/>.</param>
public sealed record EditorAgentPreview(Workflow Workflow, IReadOnlyDictionary<string, string> MarksByBlockId)
{
    public const string ADDED = "added";
    public const string CHANGED = "changed";
}

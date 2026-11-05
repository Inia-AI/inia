namespace IniaAi.Core.Workflows.Activity;

/// <summary>
/// Makes sure a workflow version carries its activity script, generating and storing one in the
/// background when it does not. Fire-and-forget by contract: callers are on the submit path and
/// must never wait on, or fail because of, script generation.
/// </summary>
public interface IWorkflowActivityScriptEnsurer
{
    void EnsureScriptInBackground(string workflowVersionId, string workflowTypeId);
}

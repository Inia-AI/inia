namespace IniaAi.Common.Interfaces.Services.AgentRuntime;

/// <summary>
/// Which of the runtime's admission classes a run is admitted under (wire <c>admissionClass</c>,
/// ADR 0142 in agent-runtime). Chat turns keep a small reserve of run slots that workflow runs
/// cannot take, and a cap they cannot pass, so neither starves the other. A run that names no
/// class is a workflow run.
/// </summary>
public enum AgentRunAdmissionClass
{
    Workflow,
    Chat,
}

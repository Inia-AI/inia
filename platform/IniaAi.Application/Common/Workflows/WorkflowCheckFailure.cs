namespace IniaAi.Application.Common.Workflows;

/// <summary>The template-save check a <see cref="WorkflowCheckFailure"/> came from.</summary>
public enum WorkflowCheck
{
    Schema = 0,
    Structure = 1,
    Library = 2,
    AgentTags = 3,
    AgentGates = 4,
    Screening = 5,
    SignatureFields = 6,
}

/// <summary>One failure of a template-save check, worded for the person who fixes the workflow.</summary>
public sealed record WorkflowCheckFailure(WorkflowCheck Check, string Message);

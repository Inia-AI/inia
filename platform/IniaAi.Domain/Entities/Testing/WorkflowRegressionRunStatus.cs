namespace IniaAi.Domain.Entities.Testing;

public enum WorkflowRegressionRunStatus
{
    Pending = 0,
    Running = 1,
    Comparing = 2,
    Completed = 3,
    Failed = 4,
    Inconclusive = 5
}

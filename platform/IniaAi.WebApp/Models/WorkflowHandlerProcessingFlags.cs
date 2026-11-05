namespace IniaAi.WebApp.Models;

[Flags]
public enum WorkflowHandlerProcessingFlags
{
    None = 0,
    SmartFilter = 1 << 0,
}

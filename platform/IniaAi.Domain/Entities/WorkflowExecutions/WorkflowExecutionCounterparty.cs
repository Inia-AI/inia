using IniaAi.Domain.Entities.Counterparties;

namespace IniaAi.Domain.Entities.WorkflowExecutions;

public class WorkflowExecutionCounterparty
{
    public string WorkflowExecutionId { get; set; } = null!;

    public string CounterpartyId { get; set; } = null!;

    #region Navigational Properties
    public WorkflowExecution WorkflowExecution { get; set; } = null!;
    public Counterparty Counterparty { get; set; } = null!;

    #endregion Navigational Properties
}

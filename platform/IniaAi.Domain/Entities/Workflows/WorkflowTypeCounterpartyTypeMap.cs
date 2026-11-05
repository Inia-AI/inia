namespace IniaAi.Domain.Entities.Workflows;

public class WorkflowTypeCounterpartyTypeMap
{
    public string WorkflowTypeId { get; set; } = string.Empty;
    public string CounterpartyTypeId { get; set; } = string.Empty;

    #region Navigation Properties
    public WorkflowType? WorkflowType { get; set; }

    #endregion Navigation Properties
}

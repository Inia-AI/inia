using IniaAi.Domain.Common;

namespace IniaAi.Domain.Entities.Workflows;

public class WorkflowTypeCategory : Entity<WorkflowTypeCategory>
{
    public new string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int? DisplayOrder { get; set; }

    #region Navigational Properties
    public ICollection<WorkflowType> WorkflowTypes { get; set; } = [];

    #endregion Navigational Properties
}

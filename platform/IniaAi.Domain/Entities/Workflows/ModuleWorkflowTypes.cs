namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// The workflow types the installed closed modules add, collected once for the platform code that
/// seeds them and the code that treats them differently.
/// </summary>
public sealed class ModuleWorkflowTypes
{
    private readonly HashSet<string> _typesReadingSpreadsheetsInContext;

    public ModuleWorkflowTypes(IEnumerable<ModuleWorkflowType> moduleTypes)
    {
        All = [.. moduleTypes.DistinctBy(type => type.Id)];
        _typesReadingSpreadsheetsInContext = [.. All.Where(type => type.SpreadsheetOverviewsReadNeighbouringPages).Select(type => type.Id)];
    }

    public IReadOnlyList<ModuleWorkflowType> All { get; }

    public bool SpreadsheetOverviewsReadNeighbouringPages(string? workflowTypeId)
    {
        return workflowTypeId is not null && _typesReadingSpreadsheetsInContext.Contains(workflowTypeId);
    }
}

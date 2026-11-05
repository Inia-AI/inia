using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.SharedComponents;

namespace IniaAi.WebApp.Components.WorkflowEditor.State;

public sealed class LibraryListFilter
{
    public string? SearchText { get; set; }

    public SharedComponentKind? Kind { get; set; }

    public bool ShowRetired { get; set; }

    public string? CategoryId { get; set; }

    public bool UncategorizedOnly { get; set; }

    public string? ConsumingWorkflowTypeId { get; set; }

    public string? ConsumingWorkflowTypeCategoryId { get; set; }

    public int SecondaryFilterCount =>
        (ConsumingWorkflowTypeId is null ? 0 : 1)
        + (ConsumingWorkflowTypeCategoryId is null ? 0 : 1)
        + (ShowRetired ? 1 : 0);

    public int ActiveFilterCount =>
        (string.IsNullOrWhiteSpace(SearchText) ? 0 : 1)
        + (Kind is null ? 0 : 1)
        + (CategoryId is null && !UncategorizedOnly ? 0 : 1)
        + SecondaryFilterCount;

    public void ClearAll()
    {
        SearchText = null;
        Kind = null;
        CategoryId = null;
        UncategorizedOnly = false;
        ConsumingWorkflowTypeId = null;
        ConsumingWorkflowTypeCategoryId = null;
        ShowRetired = false;
    }

    public IReadOnlyList<SharedComponentSummaryDto> Apply(IEnumerable<SharedComponentSummaryDto> components)
    {
        string? search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();

        return [.. components.Where(c =>
            ShowsState(c)
            && (Kind is null || c.Kind == Kind)
            && (CategoryId is null || string.Equals(c.CategoryId, CategoryId, StringComparison.Ordinal))
            && (!UncategorizedOnly || c.CategoryId is null)
            && (ConsumingWorkflowTypeId is null || c.ConsumingWorkflowTypes.Any(t =>
                string.Equals(t.WorkflowTypeId, ConsumingWorkflowTypeId, StringComparison.Ordinal)))
            && (ConsumingWorkflowTypeCategoryId is null || c.ConsumingWorkflowTypes.Any(t =>
                string.Equals(t.CategoryId, ConsumingWorkflowTypeCategoryId, StringComparison.Ordinal)))
            && (search is null || Matches(c, search)))];
    }

    public bool ShowsState(SharedComponentSummaryDto component)
    {
        return ShowRetired || component.State != SharedComponentState.Retired;
    }

    private static bool Matches(SharedComponentSummaryDto component, string search)
    {
        return component.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || component.Description?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
    }
}

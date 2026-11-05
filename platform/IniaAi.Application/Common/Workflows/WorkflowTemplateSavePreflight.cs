using ErrorOr;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.SharedComponents.Enums;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Specifications.Workflows;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Common.Workflows;

/// <summary>
/// The read-only checks a template save runs before it writes anything: shared-component reference
/// resolution, workflow-type existence, and category resolution. Shared by the save itself and by
/// the validate-only preflight so the two can never disagree about what a payload would be rejected
/// for. Nothing here writes; the head read goes through the same specification the save pipeline
/// uses, so it returns a tracked entity that this path never mutates.
/// </summary>
public class WorkflowTemplateSavePreflight
{
    public const string DEFAULT_CATEGORY_ID = "monitoring-category";

    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public WorkflowTemplateSavePreflight(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<WorkflowTypePresence> ReadPresenceAsync(string workflowTypeId, CancellationToken cancellationToken)
    {
        return await WorkflowTypePresenceQuery.ReadAsync(
            _coreUnitOfWork.Repository<WorkflowType>().GetAll(),
            workflowTypeId,
            cancellationToken);
    }

    public async Task<WorkflowVersion?> ReadHeadAsync(string workflowTypeId, CancellationToken cancellationToken)
    {
        return await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(new LatestWorkflowVersionByTypeSpec(workflowTypeId), cancellationToken);
    }

    public async Task<ErrorOr<string?>> ResolveCategoryAsync(
        string? requestedCategoryId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedCategoryId))
        {
            bool exists = await CategoryExistsAsync(requestedCategoryId, cancellationToken);

            return exists
                ? requestedCategoryId
                : Error.Validation(
                    "NewWorkflowType.CategoryId",
                    $"Workflow type category '{requestedCategoryId}' does not exist.");
        }

        bool defaultExists = await CategoryExistsAsync(DEFAULT_CATEGORY_ID, cancellationToken);

        return defaultExists ? DEFAULT_CATEGORY_ID : null;
    }

    public async Task<List<Error>> ValidateSharedComponentReferencesAsync(
        Workflow workflow,
        CancellationToken cancellationToken)
    {
        var references = new List<(string Path, string ComponentId, string? StableId, SharedComponentKind ExpectedKind)>();
        List<Section> sections = [.. workflow.Sections];
        for (int sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            Section section = sections[sectionIndex];
            string sectionPath = $"Sections[{sectionIndex}]";
            if (!string.IsNullOrWhiteSpace(section.SharedComponentId))
            {
                references.Add((
                    sectionPath,
                    section.SharedComponentId,
                    section.StableSectionId,
                    SharedComponentKind.Section));
            }

            List<Block> blocks = [.. section.Blocks];
            for (int blockIndex = 0; blockIndex < blocks.Count; blockIndex++)
            {
                Block block = blocks[blockIndex];
                if (!string.IsNullOrWhiteSpace(block.SharedComponentId))
                {
                    references.Add((
                        $"{sectionPath}.Blocks[{blockIndex}]",
                        block.SharedComponentId,
                        block.StableBlockId,
                        SharedComponentKind.Block));
                }
            }
        }

        if (references.Count == 0)
        {
            return [];
        }

        List<string> referencedIds = [.. references.Select(reference => reference.ComponentId).Distinct()];
        Dictionary<string, SharedComponentKind> registry = await _coreUnitOfWork.Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .Where(component => referencedIds.Contains(component.Id))
            .ToDictionaryAsync(component => component.Id, component => component.Kind, cancellationToken);

        List<Error> errors = [];
        foreach ((string path, string componentId, string? stableId, SharedComponentKind expectedKind) in references)
        {
            if (!registry.TryGetValue(componentId, out SharedComponentKind actualKind))
            {
                errors.Add(Error.Validation(
                    $"{path}.SharedComponentId",
                    $"References shared component '{componentId}', which does not exist in this " +
                    "deployment's Library. SharedComponent* fields are server-managed — do not " +
                    $"fabricate or copy them; remove them from this node (stable id '{stableId}') " +
                    $"and retry. [{SharedComponentErrorCodes.UNKNOWN_COMPONENT}]"));
            }
            else if (actualKind != expectedKind)
            {
                errors.Add(Error.Validation(
                    $"{path}.SharedComponentId",
                    $"References shared component '{componentId}' of kind {actualKind}, but this " +
                    $"node is a {expectedKind}. [{SharedComponentErrorCodes.KIND_MISMATCH}]"));
            }
        }

        return errors;
    }

    public async Task<bool> CategoryExistsAsync(string categoryId, CancellationToken cancellationToken)
    {
        return await _coreUnitOfWork.Repository<WorkflowTypeCategory>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(category => category.Id == categoryId, cancellationToken);
    }
}

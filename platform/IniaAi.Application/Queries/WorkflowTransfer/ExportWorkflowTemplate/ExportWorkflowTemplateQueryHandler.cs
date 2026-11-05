using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Errors;
using IniaAi.Application.Queries.SharedComponents;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.SharedComponents;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.WebApi.Contracts.Dto.WorkflowTransfer;
using Microsoft.EntityFrameworkCore;
using WireKind = IniaAi.WebApi.Contracts.Enums.SharedComponentKind;

namespace IniaAi.Application.Queries.WorkflowTransfer.ExportWorkflowTemplate;

public class ExportWorkflowTemplateQueryHandler
    : IRequestHandler<ExportWorkflowTemplateQuery, ErrorOr<ExportWorkflowTemplateResult>>
{
    public const string NOT_FOUND_CODE = WorkflowTypeErrorFactory.NOT_FOUND_CODE;

    private readonly ICoreUnitOfWork _coreUnitOfWork;

    public ExportWorkflowTemplateQueryHandler(ICoreUnitOfWork coreUnitOfWork)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
    }

    public async Task<ErrorOr<ExportWorkflowTemplateResult>> Handle(
        ExportWorkflowTemplateQuery request,
        CancellationToken cancellationToken)
    {
        WorkflowType? workflowType = await _coreUnitOfWork.Repository<WorkflowType>()
            .GetAll()
            .AsNoTracking()
            .Where(WorkflowType.NotRetired)
            .FirstOrDefaultAsync(type => type.Id == request.WorkflowTypeId, cancellationToken);
        if (workflowType is null)
        {
            return Error.NotFound(
                NOT_FOUND_CODE,
                $"Workflow type '{request.WorkflowTypeId}' does not exist.");
        }

        WorkflowVersion? head = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetFirstOrDefaultAsync(new LatestWorkflowVersionByTypeSpec(request.WorkflowTypeId), cancellationToken);
        if (head is null)
        {
            return Error.NotFound(
                NOT_FOUND_CODE,
                $"Workflow type '{request.WorkflowTypeId}' has no template content to export.");
        }

        if (head.PayloadTrimmedAt is not null)
        {
            return Error.NotFound(
                SharedComponentErrorCodes.PAYLOAD_ABSENT,
                $"The template head for '{request.WorkflowTypeId}' has been trimmed and cannot be exported.");
        }

        string templateJson = head.WorkflowJson;
        Workflow? template = Workflow.FromJson(templateJson);
        if (template is null)
        {
            return Error.Unexpected(
                SharedComponentErrorCodes.CORRUPT_DEFINITION,
                $"The stored template head for '{request.WorkflowTypeId}' could not be parsed.");
        }

        List<string> templateHashTrail = await _coreUnitOfWork.Repository<WorkflowVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(version => version.WorkflowTypeId == request.WorkflowTypeId)
            .OrderBy(version => version.CreatedAt)
            .ThenBy(version => version.ContentHash)
            .ThenBy(version => version.Id)
            .Select(version => version.ContentHash)
            .ToListAsync(cancellationToken);

        IReadOnlyList<string> componentIds =
            await SharedComponentClosure.CollectAsync(_coreUnitOfWork, template, cancellationToken);

        ErrorOr<List<TransferComponentDto>> components =
            await BuildComponentsAsync(componentIds, cancellationToken);
        if (components.IsError)
        {
            return components.Errors;
        }

        List<TransferCategoryDto> categories = await BuildCategoriesAsync(components.Value, cancellationToken);

        return new ExportWorkflowTemplateResult(
            new WorkflowTransferBundle(
                WorkflowTransferBundleFormat.CURRENT_VERSION,
                request.WorkflowTypeId,
                workflowType.Name,
                workflowType.Description,
                DateTime.UtcNow,
                request.UserId,
                templateJson,
                templateHashTrail,
                components.Value,
                categories),
            SharedComponentFanOutService.SKIPPED_CARRIERS);
    }

    private async Task<ErrorOr<List<TransferComponentDto>>> BuildComponentsAsync(
        IReadOnlyList<string> componentIds,
        CancellationToken cancellationToken)
    {
        if (componentIds.Count == 0)
        {
            return new List<TransferComponentDto>();
        }

        Dictionary<string, SharedComponent> componentsById = (await _coreUnitOfWork
            .Repository<SharedComponent>()
            .GetAll()
            .AsNoTracking()
            .Where(component => componentIds.Contains(component.Id))
            .ToListAsync(cancellationToken))
            .ToDictionary(component => component.Id, StringComparer.Ordinal);

        Dictionary<string, SharedComponentVersion> headsById =
            await SharedComponentProjections.GetHeadVersionRowsAsync(_coreUnitOfWork, componentIds, cancellationToken);

        Dictionary<string, List<string>> hashTrailsById = (await _coreUnitOfWork
            .Repository<SharedComponentVersion>()
            .GetAll()
            .AsNoTracking()
            .Where(version => componentIds.Contains(version.SharedComponentId))
            .Select(version => new { version.SharedComponentId, version.SequenceNumber, version.ContentHash })
            .ToListAsync(cancellationToken))
            .GroupBy(row => row.SharedComponentId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(row => row.SequenceNumber).Select(row => row.ContentHash).ToList(),
                StringComparer.Ordinal);

        var components = new List<TransferComponentDto>();
        foreach (string componentId in componentIds)
        {
            if (!componentsById.TryGetValue(componentId, out SharedComponent? component)
                || !headsById.TryGetValue(componentId, out SharedComponentVersion? head)
                || !hashTrailsById.TryGetValue(componentId, out List<string>? hashTrail))
            {
                return Error.Unexpected(
                    SharedComponentErrorCodes.CORRUPT_DEFINITION,
                    $"Component '{componentId}' is required by the template but has no exportable content.");
            }

            if (head.PayloadAbsentAt is not null)
            {
                return Error.NotFound(
                    SharedComponentErrorCodes.PAYLOAD_ABSENT,
                    $"Component '{componentId}' has no stored head payload and cannot be exported.");
            }

            components.Add(new TransferComponentDto(
                component.Id,
                (WireKind)(int)component.Kind,
                component.Name,
                component.Description,
                component.CategoryId,
                head.ContentHash,
                head.ContentJson,
                hashTrail));
        }

        return components;
    }

    private async Task<List<TransferCategoryDto>> BuildCategoriesAsync(
        List<TransferComponentDto> components,
        CancellationToken cancellationToken)
    {
        List<string> categoryIds = [.. components
            .Select(component => component.CategoryId)
            .Where(categoryId => !string.IsNullOrEmpty(categoryId))
            .Select(categoryId => categoryId!)
            .Distinct(StringComparer.Ordinal)];
        if (categoryIds.Count == 0)
        {
            return [];
        }

        return [.. (await _coreUnitOfWork
            .Repository<SharedComponentCategory>()
            .GetAll()
            .AsNoTracking()
            .Where(category => categoryIds.Contains(category.Id))
            .ToListAsync(cancellationToken))
            .Select(category => new TransferCategoryDto(category.Id, category.Name, category.Description))
            .OrderBy(category => category.Id, StringComparer.Ordinal)];
    }
}

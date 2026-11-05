using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.WorkflowTemplates.UpdateDocumentClass;

public class UpdateDocumentClassCommandHandler
    : IRequestHandler<UpdateDocumentClassCommand, ErrorOr<DocumentClassMutationResult>>
{
    private readonly ISender _sender;
    private readonly IDocumentClassEditor _editor;

    public UpdateDocumentClassCommandHandler(ISender sender, IDocumentClassEditor editor)
    {
        _sender = sender;
        _editor = editor;
    }

    public async Task<ErrorOr<DocumentClassMutationResult>> Handle(
        UpdateDocumentClassCommand request,
        CancellationToken cancellationToken)
    {
        ErrorOr<WorkflowTemplateContentResult> content = await _sender.Send(
            new GetWorkflowTemplateContentQuery(request.OrganizationId, request.UserId, request.WorkflowTypeId),
            cancellationToken);
        if (content.IsError)
        {
            return content.Errors;
        }

        var workflow = Workflow.FromJson(content.Value.WorkflowJson);
        if (workflow is null)
        {
            return Error.Unexpected(
                "WorkflowTemplate.Corrupt",
                "The stored workflow template could not be read.");
        }

        DocumentClass? existing = workflow.DocumentClasses.FirstOrDefault(
            dc => dc.Key.Equals(request.Key, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return Error.NotFound(
                "DocumentClass.NotFound",
                $"Document class '{request.Key}' was not found.");
        }

        var edit = new DocumentClassEdit(
            OriginalKey: existing.Key,
            Key: existing.Key,
            DisplayName: request.DisplayName ?? existing.DisplayName,
            Description: request.Description ?? existing.Description,
            DocumentCategoryId: ResolveDocumentCategoryId(request.DocumentCategoryId, existing.DocumentCategoryId),
            Requirement: request.Requirement ?? existing.Requirement,
            ShouldBeSingular: request.ShouldBeSingular ?? existing.ShouldBeSingular,
            IsOutputDocument: request.IsOutputDocument ?? existing.IsOutputDocument,
            LlmDescription: request.LlmDescription ?? existing.LlmDescription);

        ErrorOr<DocumentClass> updated = _editor.Upsert(workflow, edit);
        if (updated.IsError)
        {
            return updated.Errors;
        }

        ErrorOr<SaveWorkflowResult> saved = await _sender.Send(
            new SaveWorkflowCommand(
                request.OrganizationId,
                request.UserId,
                request.WorkflowTypeId,
                workflow,
                request.BaseVersionId,
                EnforceOptimisticConcurrency: true),
            cancellationToken);
        if (saved.IsError)
        {
            return saved.Errors;
        }

        return new DocumentClassMutationResult(
            saved.Value.VersionId,
            saved.Value.ContentHash,
            saved.Value.WasDeduped,
            updated.Value.Key,
            DocumentClassView.From(updated.Value));
    }

    private static string? ResolveDocumentCategoryId(string? requested, string? current)
    {
        if (requested is null)
        {
            return current;
        }

        return requested.Length == 0 ? null : requested;
    }
}

using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.WorkflowTemplates.RenameDocumentClass;

public class RenameDocumentClassCommandHandler
    : IRequestHandler<RenameDocumentClassCommand, ErrorOr<DocumentClassMutationResult>>
{
    private readonly ISender _sender;
    private readonly IDocumentClassEditor _editor;

    public RenameDocumentClassCommandHandler(ISender sender, IDocumentClassEditor editor)
    {
        _sender = sender;
        _editor = editor;
    }

    public async Task<ErrorOr<DocumentClassMutationResult>> Handle(
        RenameDocumentClassCommand request,
        CancellationToken cancellationToken)
    {
        ErrorOr<WorkflowTemplateContentResult> content = await _sender.Send(
            new GetWorkflowTemplateContentQuery(request.OrganizationId, request.UserId, request.WorkflowTypeId),
            cancellationToken);
        if (content.IsError)
        {
            return content.Errors;
        }

        Workflow? workflow = Workflow.FromJson(content.Value.WorkflowJson);
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
            Key: request.NewKey,
            DisplayName: existing.DisplayName,
            Description: existing.Description,
            DocumentCategoryId: existing.DocumentCategoryId,
            Requirement: existing.Requirement,
            ShouldBeSingular: existing.ShouldBeSingular,
            IsOutputDocument: existing.IsOutputDocument,
            LlmDescription: existing.LlmDescription);

        ErrorOr<DocumentClass> renamed = _editor.Upsert(workflow, edit);
        if (renamed.IsError)
        {
            return renamed.Errors;
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
            renamed.Value.Key,
            DocumentClassView.From(renamed.Value));
    }
}

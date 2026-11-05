using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.WorkflowTemplates.CreateDocumentClass;

public class CreateDocumentClassCommandHandler
    : IRequestHandler<CreateDocumentClassCommand, ErrorOr<DocumentClassMutationResult>>
{
    private readonly ISender _sender;
    private readonly IDocumentClassEditor _editor;

    public CreateDocumentClassCommandHandler(ISender sender, IDocumentClassEditor editor)
    {
        _sender = sender;
        _editor = editor;
    }

    public async Task<ErrorOr<DocumentClassMutationResult>> Handle(
        CreateDocumentClassCommand request,
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

        var edit = new DocumentClassEdit(
            OriginalKey: null,
            Key: request.Key,
            DisplayName: request.DisplayName,
            Description: request.Description,
            DocumentCategoryId: string.IsNullOrEmpty(request.DocumentCategoryId) ? null : request.DocumentCategoryId,
            Requirement: request.Requirement,
            ShouldBeSingular: request.ShouldBeSingular,
            IsOutputDocument: request.IsOutputDocument,
            LlmDescription: request.LlmDescription);

        ErrorOr<DocumentClass> created = _editor.Upsert(workflow, edit);
        if (created.IsError)
        {
            return created.Errors;
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
            created.Value.Key,
            DocumentClassView.From(created.Value));
    }
}

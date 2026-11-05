using ErrorOr;
using IniaAi.Application.Commands.Workflows.SaveWorkflow;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;
using IniaAi.Application.Queries.WorkflowTemplates.GetWorkflowTemplateContent;
using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Application.Commands.WorkflowTemplates.DeleteDocumentClass;

public class DeleteDocumentClassCommandHandler
    : IRequestHandler<DeleteDocumentClassCommand, ErrorOr<DocumentClassMutationResult>>
{
    private readonly ISender _sender;
    private readonly IDocumentClassEditor _editor;

    public DeleteDocumentClassCommandHandler(ISender sender, IDocumentClassEditor editor)
    {
        _sender = sender;
        _editor = editor;
    }

    public async Task<ErrorOr<DocumentClassMutationResult>> Handle(
        DeleteDocumentClassCommand request,
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

        ErrorOr<Deleted> removed = _editor.Remove(workflow, request.Key);
        if (removed.IsError)
        {
            return removed.Errors;
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
            request.Key);
    }
}

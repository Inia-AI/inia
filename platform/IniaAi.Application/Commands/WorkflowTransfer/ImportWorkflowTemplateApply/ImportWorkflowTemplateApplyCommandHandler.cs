using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Workflows;

namespace IniaAi.Application.Commands.WorkflowTransfer.ImportWorkflowTemplateApply;

public class ImportWorkflowTemplateApplyCommandHandler
    : IRequestHandler<ImportWorkflowTemplateApplyCommand, ErrorOr<ImportWorkflowTemplateApplyResult>>
{
    private readonly SharedComponentFanOutService _fanOutService;

    public ImportWorkflowTemplateApplyCommandHandler(SharedComponentFanOutService fanOutService)
    {
        _fanOutService = fanOutService ?? throw new ArgumentNullException(nameof(fanOutService));
    }

    public async Task<ErrorOr<ImportWorkflowTemplateApplyResult>> Handle(
        ImportWorkflowTemplateApplyCommand request,
        CancellationToken cancellationToken)
    {
        return await _fanOutService.ImportApplyAsync(request, cancellationToken);
    }
}

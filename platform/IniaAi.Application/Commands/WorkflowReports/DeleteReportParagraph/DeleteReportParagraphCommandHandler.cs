using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowReports.DeleteReportParagraph;

/// <summary>
/// Handles <see cref="DeleteReportParagraphCommand"/> by removing a report paragraph.
/// </summary>
public class DeleteReportParagraphCommandHandler : IRequestHandler<DeleteReportParagraphCommand, ErrorOr<Success>>
{
    private readonly ILogger<DeleteReportParagraphCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public DeleteReportParagraphCommandHandler(
        ILogger<DeleteReportParagraphCommandHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<Success>> Handle(DeleteReportParagraphCommand request, CancellationToken cancellationToken)
    {
        var spec = new ReportParagraphByIdSpec(request.ParagraphId, request.Id, request.OrganizationId);
        ReportParagraph? paragraph = await _userDataUnitOfWork.Repository<ReportParagraph>()
            .GetFirstOrDefaultAsync(spec, cancellationToken);
        if (paragraph is null)
        {
            _logger.LogWarning("ReportParagraph {ParagraphId} not found for execution {ExecutionId}", LogSanitizer.ForLog(request.ParagraphId), LogSanitizer.ForLog(request.Id));
            return Error.NotFound(description: "Report paragraph not found.");
        }

        _userDataUnitOfWork.Repository<ReportParagraph>().Remove(paragraph);
        _ = await _userDataUnitOfWork.CompleteAsync(cancellationToken);
        return Result.Success;
    }
}

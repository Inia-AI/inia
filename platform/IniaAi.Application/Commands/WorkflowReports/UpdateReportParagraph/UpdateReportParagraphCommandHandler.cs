using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.WorkflowReports.UpdateReportParagraph;

/// <summary>
/// Handles <see cref="UpdateReportParagraphCommand"/> by updating a report paragraph's editable fields.
/// </summary>
public class UpdateReportParagraphCommandHandler : IRequestHandler<UpdateReportParagraphCommand, ErrorOr<Success>>
{
    private readonly ILogger<UpdateReportParagraphCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public UpdateReportParagraphCommandHandler(
        ILogger<UpdateReportParagraphCommandHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<Success>> Handle(UpdateReportParagraphCommand request, CancellationToken cancellationToken)
    {
        var spec = new ReportParagraphByIdSpec(request.ParagraphId, request.Id, request.OrganizationId);
        ReportParagraph? paragraph = await _userDataUnitOfWork.Repository<ReportParagraph>()
            .GetFirstOrDefaultAsync(spec, cancellationToken);
        if (paragraph is null)
        {
            _logger.LogWarning("ReportParagraph {ParagraphId} not found for execution {ExecutionId}", LogSanitizer.ForLog(request.ParagraphId), LogSanitizer.ForLog(request.Id));
            return Error.NotFound(description: "Report paragraph not found.");
        }

        if (request.Request.Title is not null)
        {
            paragraph.Title = request.Request.Title;
        }

        if (request.Request.Subtitle is not null)
        {
            paragraph.Subtitle = request.Request.Subtitle;
        }

        if (request.Request.Confidence.HasValue)
        {
            paragraph.Confidence = request.Request.Confidence.Value;
        }

        if (request.Request.Content is not null)
        {
            paragraph.Content = request.Request.Content;
        }

        _ = await _userDataUnitOfWork.CompleteAsync(cancellationToken);
        return Result.Success;
    }
}

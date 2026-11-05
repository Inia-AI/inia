using ErrorOr;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Specifications.ReportFeedback;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.ReportFeedback.DeleteReportFeedback;

/// <summary>
/// Handles <see cref="DeleteReportFeedbackCommand"/>. Nothing keys on a feedback item, so the row
/// is removed outright; an editor agent message that was sent the item keeps its own copy.
/// </summary>
public class DeleteReportFeedbackCommandHandler : IRequestHandler<DeleteReportFeedbackCommand, ErrorOr<Deleted>>
{
    private readonly ILogger<DeleteReportFeedbackCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;

    public DeleteReportFeedbackCommandHandler(
        ILogger<DeleteReportFeedbackCommandHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<Deleted>> Handle(DeleteReportFeedbackCommand request, CancellationToken cancellationToken)
    {
        IRepository<ReportFeedbackItem> repository = _userDataUnitOfWork.Repository<ReportFeedbackItem>();
        ReportFeedbackItem? item = await repository.GetFirstOrDefaultAsync(
            new ReportFeedbackByIdTrackedSpec(request.FeedbackId, request.WorkflowExecutionId, request.OrganizationId),
            cancellationToken);

        if (item is null)
        {
            _logger.LogWarning(
                "Feedback {FeedbackId} not found on execution {ExecutionId} for organization {OrganizationId}",
                LogSanitizer.ForLog(request.FeedbackId),
                LogSanitizer.ForLog(request.WorkflowExecutionId),
                request.OrganizationId);

            return Error.NotFound(description: "Feedback item not found.");
        }

        repository.HardRemove(item);
        _ = await _userDataUnitOfWork.CompleteAsync(cancellationToken);

        _logger.LogInformation(
            "Feedback {FeedbackId} on execution {ExecutionId} deleted by user {UserId}",
            item.Id,
            item.WorkflowExecutionId,
            request.UserId);

        return Result.Deleted;
    }
}

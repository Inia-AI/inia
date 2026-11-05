using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Mappings;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Specifications.ReportFeedback;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using IniaAi.Application.Common.Dispatching;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.ReportFeedback.UpdateReportFeedbackStatus;

/// <summary>
/// Handles <see cref="UpdateReportFeedbackStatusCommand"/>.
/// </summary>
public class UpdateReportFeedbackStatusCommandHandler : IRequestHandler<UpdateReportFeedbackStatusCommand, ErrorOr<ReportFeedbackDto>>
{
    private readonly ILogger<UpdateReportFeedbackStatusCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IUserDisplayNameResolver _nameResolver;

    public UpdateReportFeedbackStatusCommandHandler(
        ILogger<UpdateReportFeedbackStatusCommandHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork,
        IUserDisplayNameResolver nameResolver)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
        _nameResolver = nameResolver;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<ReportFeedbackDto>> Handle(UpdateReportFeedbackStatusCommand request, CancellationToken cancellationToken)
    {
        ReportFeedbackItem? item = await _userDataUnitOfWork.Repository<ReportFeedbackItem>()
            .GetFirstOrDefaultAsync(
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

        item.SetStatus((ReportFeedbackStatus)request.Request.Status, request.Request.DismissReason, request.UserId);
        _ = await _userDataUnitOfWork.CompleteAsync(cancellationToken);

        IReadOnlyDictionary<string, string> names = await _nameResolver.ResolveAsync([item.AuthorUserId], cancellationToken);

        return ReportFeedbackDtoMapper.ToDto(item, names, includeOrganizationId: false);
    }
}

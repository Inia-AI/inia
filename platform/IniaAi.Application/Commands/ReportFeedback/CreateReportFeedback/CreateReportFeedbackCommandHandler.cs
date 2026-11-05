using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Mappings;
using IniaAi.Common.Text;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Specifications.WorkflowExecutions;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using IniaAi.Application.Common.Dispatching;
using Microsoft.Extensions.Logging;

namespace IniaAi.Application.Commands.ReportFeedback.CreateReportFeedback;

/// <summary>
/// Handles <see cref="CreateReportFeedbackCommand"/>: resolves the execution within the caller's
/// organization, copies its workflow type onto the item, and stores the item.
/// </summary>
public class CreateReportFeedbackCommandHandler : IRequestHandler<CreateReportFeedbackCommand, ErrorOr<ReportFeedbackDto>>
{
    private readonly ILogger<CreateReportFeedbackCommandHandler> _logger;
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IUserDisplayNameResolver _nameResolver;

    public CreateReportFeedbackCommandHandler(
        ILogger<CreateReportFeedbackCommandHandler> logger,
        IUserDataUnitOfWork userDataUnitOfWork,
        IUserDisplayNameResolver nameResolver)
    {
        _logger = logger;
        _userDataUnitOfWork = userDataUnitOfWork;
        _nameResolver = nameResolver;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<ReportFeedbackDto>> Handle(CreateReportFeedbackCommand request, CancellationToken cancellationToken)
    {
        WorkflowExecution? execution = await _userDataUnitOfWork.Repository<WorkflowExecution>()
            .GetFirstOrDefaultAsync(
                new WorkflowExecutionByIdAndOrganizationSpec(request.WorkflowExecutionId, request.OrganizationId),
                cancellationToken);

        if (execution is null)
        {
            _logger.LogWarning(
                "Workflow execution {ExecutionId} not found for organization {OrganizationId} when recording feedback",
                LogSanitizer.ForLog(request.WorkflowExecutionId),
                request.OrganizationId);

            return Error.NotFound(description: "Workflow report not found.");
        }

        ReportFeedbackItem item = ReportFeedbackItem.Create(
            request.OrganizationId,
            request.WorkflowExecutionId,
            execution.WorkflowTypeId,
            request.Request.QuotedText,
            (ReportFeedbackCategory)request.Request.Category,
            request.Request.Comment,
            request.UserId);

        await _userDataUnitOfWork.Repository<ReportFeedbackItem>().AddAsync(item);
        _ = await _userDataUnitOfWork.CompleteAsync(cancellationToken);

        IReadOnlyDictionary<string, string> names = await _nameResolver.ResolveAsync([item.AuthorUserId], cancellationToken);

        return ReportFeedbackDtoMapper.ToDto(item, names, includeOrganizationId: false);
    }
}

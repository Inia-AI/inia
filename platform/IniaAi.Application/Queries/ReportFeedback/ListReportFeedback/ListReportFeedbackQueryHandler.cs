using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Mappings;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Specifications.ReportFeedback;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Queries.ReportFeedback.ListReportFeedback;

/// <summary>
/// Handles <see cref="ListReportFeedbackQuery"/>: the org-scoped read of one report's feedback.
/// </summary>
public class ListReportFeedbackQueryHandler : IRequestHandler<ListReportFeedbackQuery, ErrorOr<List<ReportFeedbackDto>>>
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IUserDisplayNameResolver _nameResolver;

    public ListReportFeedbackQueryHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        IUserDisplayNameResolver nameResolver)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _nameResolver = nameResolver;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<List<ReportFeedbackDto>>> Handle(ListReportFeedbackQuery request, CancellationToken cancellationToken)
    {
        IList<ReportFeedbackItem> items = await _userDataUnitOfWork.Repository<ReportFeedbackItem>()
            .GetAllAsync(new ReportFeedbackByExecutionSpec(request.WorkflowExecutionId, request.OrganizationId), cancellationToken);

        IReadOnlyDictionary<string, string> names = await _nameResolver.ResolveAsync(
            [.. items.Select(item => item.AuthorUserId)],
            cancellationToken);

        return items
            .Select(item => ReportFeedbackDtoMapper.ToDto(item, names, includeOrganizationId: false))
            .ToList();
    }
}

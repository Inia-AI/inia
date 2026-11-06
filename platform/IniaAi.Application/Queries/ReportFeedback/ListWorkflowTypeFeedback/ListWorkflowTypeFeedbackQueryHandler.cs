using IniaAi.Application.Common.Capabilities;
using ErrorOr;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Application.Common.Mappings;
using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.Domain.Specifications.ReportFeedback;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Queries.ReportFeedback.ListWorkflowTypeFeedback;

/// <summary>
/// Handles <see cref="ListWorkflowTypeFeedbackQuery"/>: the SuperAdmin-only, cross-organization read.
/// </summary>
public class ListWorkflowTypeFeedbackQueryHandler : IRequestHandler<ListWorkflowTypeFeedbackQuery, ErrorOr<ReportFeedbackPageDto>>
{
    public const string SUPER_ADMIN_ONLY = "Only a SuperAdmin may read feedback across organizations.";

    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly IUserDisplayNameResolver _nameResolver;
    private readonly IOrganizationsClosedToStaff _closed;

    public ListWorkflowTypeFeedbackQueryHandler(
        IUserDataUnitOfWork userDataUnitOfWork,
        IUserDisplayNameResolver nameResolver,
        IOrganizationsClosedToStaff closed)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _nameResolver = nameResolver;
        _closed = closed;
    }

    /// <inheritdoc />
    public async Task<ErrorOr<ReportFeedbackPageDto>> Handle(ListWorkflowTypeFeedbackQuery request, CancellationToken cancellationToken)
    {
        if (!request.CallerIsSuperAdmin)
        {
            return Error.Forbidden(description: SUPER_ADMIN_ONLY);
        }

        DateTime? cursorCreatedAtUtc = null;
        string? cursorId = null;

        if (ReportFeedbackCursor.TryDecode(request.Cursor, out DateTime decodedCreatedAt, out string decodedId))
        {
            cursorCreatedAtUtc = decodedCreatedAt;
            cursorId = decodedId;
        }

        ReportFeedbackStatus? status = request.Status.HasValue
            ? (ReportFeedbackStatus)request.Status.Value
            : null;

        IList<ReportFeedbackItem> rows = await _userDataUnitOfWork.Repository<ReportFeedbackItem>()
            .GetAllAsync(
                new ReportFeedbackByWorkflowTypePagedSpec(
                    request.WorkflowTypeId,
                    status,
                    request.Take + 1,
                    cursorCreatedAtUtc,
                    cursorId,
                    _closed.ClosedToAllStaff()),
                cancellationToken);

        bool hasMore = rows.Count > request.Take;
        List<ReportFeedbackItem> page = [.. rows.Take(request.Take)];

        IReadOnlyDictionary<string, string> names = await _nameResolver.ResolveAsync(
            [.. page.Select(item => item.AuthorUserId)],
            cancellationToken);

        ReportFeedbackItem? last = page.Count > 0 ? page[^1] : null;
        string? nextCursor = hasMore && last is not null
            ? ReportFeedbackCursor.Encode(last.CreatedAt, last.Id)
            : null;

        return new ReportFeedbackPageDto(
            [.. page.Select(item => ReportFeedbackDtoMapper.ToDto(item, names, includeOrganizationId: true))],
            hasMore,
            nextCursor);
    }
}

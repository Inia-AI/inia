using ErrorOr;
using FluentValidation;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Specifications.Workflows;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Queries.Workflows.Versions;

/// <summary>
/// Returns one keyset page of the type's version history, newest-first. Pass the last row of
/// the previous page as <paramref name="CursorCreatedAtUtc"/> / <paramref name="CursorId"/> to
/// fetch older versions; omit both for the newest page.
/// </summary>
/// <remarks>
/// Deliberately not gated by WorkflowTypeOrganizationAccess: it serves the role-gated authoring
/// surfaces (the workflow editor and the EDITOR_ROLES template API), which author universal
/// templates whose types may lack access rows entirely. Any HTTP mouth must carry that role gate.
/// <paramref name="RequireKnownType"/> answers an unknown type with
/// <see cref="WorkflowVersionErrors.UNKNOWN_TYPE_CODE"/> instead of an empty page, so a mistyped
/// id over HTTP does not read as "no history".
/// </remarks>
public record GetWorkflowHistoryQuery(
    string OrganizationId,
    string WorkflowTypeId,
    int Take = 30,
    DateTime? CursorCreatedAtUtc = null,
    string? CursorId = null,
    bool RequireKnownType = false)
    : IRequest<ErrorOr<WorkflowHistoryPageDto>>;

public class GetWorkflowHistoryQueryHandler
    : IRequestHandler<GetWorkflowHistoryQuery, ErrorOr<WorkflowHistoryPageDto>>
{
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IUserDisplayNameResolver _nameResolver;

    public GetWorkflowHistoryQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        IUserDisplayNameResolver nameResolver)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _nameResolver = nameResolver;
    }

    public async Task<ErrorOr<WorkflowHistoryPageDto>> Handle(
        GetWorkflowHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (request.RequireKnownType)
        {
            bool typeExists = await _coreUnitOfWork.Repository<WorkflowType>()
                .GetAll()
                .AsNoTracking()
                .Where(WorkflowType.NotRetired)
                .AnyAsync(workflowType => workflowType.Id == request.WorkflowTypeId, cancellationToken);
            if (!typeExists)
            {
                return WorkflowVersionErrors.UnknownType(request.WorkflowTypeId);
            }
        }

        IRepository<WorkflowVersion> repository = _coreUnitOfWork.Repository<WorkflowVersion>();

        // Projected so a page never pulls the compressed workflow payloads.
        List<WorkflowVersionRow> rows = await repository
            .GetAll(new WorkflowVersionsByTypePagedSpec(
                request.WorkflowTypeId,
                request.Take + 1,
                request.CursorCreatedAtUtc,
                request.CursorId))
            .Select(version => new WorkflowVersionRow(
                version.Id,
                version.CreatedAt,
                version.CreatedByUserId,
                version.ContentHash,
                version.AutoNarrativeSummary,
                version.PayloadTrimmedAt,
                version.ParentVersionId,
                version.HumanNote))
            .ToListAsync(cancellationToken);

        bool hasMore = rows.Count > request.Take;
        List<WorkflowVersionRow> versions = [.. rows.Take(request.Take)];

        int totalCount = await repository
            .GetAll(new WorkflowVersionsByTypeCountSpec(request.WorkflowTypeId))
            .CountAsync(cancellationToken);

        IReadOnlyDictionary<string, string> names = await _nameResolver.ResolveAsync(
            [.. versions.Select(version => version.CreatedByUserId)], cancellationToken);

        return new WorkflowHistoryPageDto(
            [.. versions.Select(version => ToSummary(version, names))],
            hasMore,
            totalCount);
    }

    private static WorkflowVersionSummaryDto ToSummary(
        WorkflowVersionRow version,
        IReadOnlyDictionary<string, string> names)
    {
        return new WorkflowVersionSummaryDto(
            version.Id,
            version.CreatedAt,
            version.CreatedByUserId,
            names.GetValueOrDefault(version.CreatedByUserId, version.CreatedByUserId),
            WorkflowVersionCode.Short(version.ContentHash),
            version.ContentHash,
            version.AutoNarrativeSummary,
            version.PayloadTrimmedAt)
        {
            ParentVersionId = version.ParentVersionId,
            Note = version.HumanNote,
        };
    }

    private sealed record WorkflowVersionRow(
        string Id,
        DateTime CreatedAt,
        string CreatedByUserId,
        string ContentHash,
        string? AutoNarrativeSummary,
        DateTime? PayloadTrimmedAt,
        string? ParentVersionId,
        string? HumanNote);
}

public class GetWorkflowHistoryQueryValidator : AbstractValidator<GetWorkflowHistoryQuery>
{
    public const int MAX_TAKE = 200;

    public GetWorkflowHistoryQueryValidator()
    {
        RuleFor(x => x.OrganizationId).NotEmpty();
        RuleFor(x => x.WorkflowTypeId).NotEmpty();
        RuleFor(x => x.Take).GreaterThan(0).LessThanOrEqualTo(MAX_TAKE);
        RuleFor(x => x.CursorId)
            .NotEmpty()
            .When(x => x.CursorCreatedAtUtc.HasValue)
            .WithMessage("CursorId must be supplied together with CursorCreatedAtUtc.");
        RuleFor(x => x.CursorCreatedAtUtc)
            .NotNull()
            .When(x => !string.IsNullOrEmpty(x.CursorId))
            .WithMessage("CursorCreatedAtUtc must be supplied together with CursorId.");
    }
}

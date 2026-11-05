using ErrorOr;
using IniaAi.Application.Commands.Signatures;
using IniaAi.Application.Common.Dispatching;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.Signatures;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Persistence.UserData.Repositories;
using IniaAi.Domain.Specifications.Signatures;
using IniaAi.WebApi.Contracts.Responses.Signatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IniaAi.Application.Queries.Signatures.GetSignatureHistory;

/// <summary>
/// Returns one page of <see cref="SignatureFieldOverrideAuditLog"/> rows for the
/// workflow's signature override history. Reuses the same workflow-type access gate
/// as <c>GetWorkflowSignaturesQueryHandler</c> so the same tenant boundary applies.
/// </summary>
public class GetSignatureHistoryQueryHandler
    : IRequestHandler<GetSignatureHistoryQuery, ErrorOr<SignatureHistoryPageDto>>
{
    private const string ERROR_NOT_FOUND = "Workflow.NotFound";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly ILogger<GetSignatureHistoryQueryHandler> _logger;

    public GetSignatureHistoryQueryHandler(
        ICoreUnitOfWork coreUnitOfWork,
        ILogger<GetSignatureHistoryQueryHandler> logger)
    {
        _coreUnitOfWork = coreUnitOfWork ?? throw new ArgumentNullException(nameof(coreUnitOfWork));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ErrorOr<SignatureHistoryPageDto>> Handle(
        GetSignatureHistoryQuery request,
        CancellationToken cancellationToken)
    {
        bool hasAccess = await _coreUnitOfWork.Repository<WorkflowTypeOrganizationAccess>()
            .GetAll()
            .AsNoTracking()
            .AnyAsync(
                a => a.WorkflowTypeId == request.WorkflowTypeId
                    && a.OrganizationId == request.OrganizationId,
                cancellationToken);

        if (!hasAccess)
        {
            return Error.NotFound(
                ERROR_NOT_FOUND,
                $"No workflow of type '{request.WorkflowTypeId}' was found for this organisation.");
        }

        IRepository<SignatureFieldOverrideAuditLog> repo =
            _coreUnitOfWork.Repository<SignatureFieldOverrideAuditLog>();

        var pageSpec = new SignatureFieldOverrideAuditLogHistorySpec(
            request.OrganizationId,
            request.WorkflowTypeId,
            request.PlaceholderName,
            request.ActorUserId,
            request.FromUtc,
            request.ToUtc,
            request.Skip,
            request.Take);

        var countSpec = new SignatureFieldOverrideAuditLogCountSpec(
            request.OrganizationId,
            request.WorkflowTypeId,
            request.PlaceholderName,
            request.ActorUserId,
            request.FromUtc,
            request.ToUtc);

        IList<SignatureFieldOverrideAuditLog> rows = await repo.GetAllAsync(pageSpec, cancellationToken);
        int total = await repo.GetAll(countSpec).CountAsync(cancellationToken);

        List<SignatureHistoryEntryDto> items = [];
        foreach (SignatureFieldOverrideAuditLog row in rows)
        {
            items.Add(ProjectRow(row));
        }

        return new SignatureHistoryPageDto(
            Items: items,
            TotalCount: total,
            Skip: request.Skip,
            Take: request.Take);
    }

    private SignatureHistoryEntryDto ProjectRow(SignatureFieldOverrideAuditLog row)
    {
        IReadOnlyList<SignatureHistoryApproverDto> approvers = DeserializeApprovers(row.ApproversJson, row.Id);

        return new SignatureHistoryEntryDto(
            Id: row.Id,
            BlockId: row.BlockId,
            PlaceholderName: row.PlaceholderName,
            Action: row.Action,
            PreviousValue: row.PreviousValue,
            NewValue: row.NewValue,
            ActorUserId: row.ActorUserId,
            ActorUserEmail: row.ActorUserEmail,
            Approvers: approvers,
            UsedByBlockNames: DeserializeBlockNames(row.ReferencedBlockNamesJson, row.Id),
            OccurredAt: row.CreatedAt);
    }

    private IReadOnlyList<string>? DeserializeBlockNames(string? blockNamesJson, string rowId)
    {
        if (string.IsNullOrWhiteSpace(blockNamesJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(blockNamesJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to deserialize ReferencedBlockNamesJson on SignatureFieldOverrideAuditLog row {RowId}; rendering no block context",
                rowId);
            return null;
        }
    }

    private IReadOnlyList<SignatureHistoryApproverDto> DeserializeApprovers(string? approversJson, string rowId)
    {
        if (string.IsNullOrWhiteSpace(approversJson))
        {
            return [];
        }

        try
        {
            List<SignatureHistoryApproverDto>? parsed = JsonSerializer.Deserialize<List<SignatureHistoryApproverDto>>(
                approversJson,
                SignatureOverrideWriter.ApproverJsonOptions);
            return parsed ?? [];
        }
        catch (JsonException ex)
        {
            // Malformed JSON in an audit row is symptom of writer corruption — surface
            // it via the log so observability catches it rather than silently rendering
            // "no approvers" forever. Page still renders with an empty list.
            _logger.LogWarning(
                ex,
                "Failed to deserialize ApproversJson on SignatureFieldOverrideAuditLog row {RowId}; rendering empty approver list",
                rowId);
            return [];
        }
    }
}

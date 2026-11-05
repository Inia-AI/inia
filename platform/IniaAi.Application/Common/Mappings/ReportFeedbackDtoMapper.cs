using IniaAi.Domain.Entities.ReportFeedback;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.ReportFeedback;

namespace IniaAi.Application.Common.Mappings;

/// <summary>
/// Builds the wire shape of a feedback item. The organization id is written only on the
/// cross-organization read, so an org-scoped response never echoes a tenant id it did not need.
/// </summary>
public static class ReportFeedbackDtoMapper
{
    public static ReportFeedbackDto ToDto(
        ReportFeedbackItem item,
        IReadOnlyDictionary<string, string> authorNames,
        bool includeOrganizationId)
    {
        return new ReportFeedbackDto
        {
            Id = item.Id,
            WorkflowExecutionId = item.WorkflowExecutionId,
            WorkflowTypeId = item.WorkflowTypeId,
            QuotedText = item.QuotedText,
            Category = (ReportFeedbackCategoryDto)item.Category,
            Comment = item.Comment,
            AuthorUserId = item.AuthorUserId,
            AuthorDisplayName = authorNames.GetValueOrDefault(item.AuthorUserId, item.AuthorUserId),
            Status = (ReportFeedbackStatusDto)item.Status,
            DismissReason = item.DismissReason,
            StatusChangedByUserId = item.StatusChangedByUserId,
            AddressedInVersionId = item.AddressedInVersionId,
            CreatedAt = item.CreatedAt,
            UpdatedAt = item.UpdatedAt,
            OrganizationId = includeOrganizationId ? item.OrganizationId : null,
        };
    }
}

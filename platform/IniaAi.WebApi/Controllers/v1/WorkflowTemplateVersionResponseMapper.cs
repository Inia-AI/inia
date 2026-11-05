using IniaAi.Application.Queries.Workflows.Versions;
using IniaAi.Core.Workflows.Diff;
using IniaAi.WebApi.Contracts.Enums;
using IniaAi.WebApi.Contracts.Responses.WorkflowTemplates;
using IniaAi.WebApi.Contracts.Responses.WorkflowVersions;

namespace IniaAi.WebApi.Controllers.v1;

internal static class WorkflowTemplateVersionResponseMapper
{
    internal static WorkflowTemplateVersionsResponse ToResponse(string workflowTypeId, WorkflowHistoryPageDto page)
    {
        WorkflowVersionSummaryDto? last = page.Versions.Count > 0 ? page.Versions[^1] : null;
        string? nextCursor = page.HasMore && last is not null
            ? new WorkflowVersionCursor(last.CreatedAtUtc, last.Id).Encode()
            : null;

        return new WorkflowTemplateVersionsResponse(
            workflowTypeId,
            [.. page.Versions.Select(ToDto)],
            page.TotalCount,
            page.HasMore,
            nextCursor);
    }

    internal static WorkflowTemplateVersionDiffResponse ToChangesResponse(
        string workflowTypeId,
        WorkflowVersionChangesResult changes,
        WorkflowNarrativeGenerator narrativeGenerator)
    {
        if (changes.Status == WorkflowVersionChangeStatus.Unavailable)
        {
            throw new InvalidOperationException(
                "An undiffable version has no response shape. Query with FailWhenUnavailable so it answers as an error.");
        }

        return new WorkflowTemplateVersionDiffResponse(
            workflowTypeId,
            changes.ParentVersionId,
            changes.VersionId,
            changes.Status == WorkflowVersionChangeStatus.FirstVersion,
            changes.StoredNarrative,
            changes.Diff is null ? [] : narrativeGenerator.GenerateParts(changes.Diff),
            changes.Diff is null ? null : ToDto(changes.Diff));
    }

    internal static WorkflowTemplateVersionDiffResponse ToCompareResponse(
        string workflowTypeId,
        string fromVersionId,
        string toVersionId,
        WorkflowDiff diff,
        WorkflowNarrativeGenerator narrativeGenerator)
    {
        return new WorkflowTemplateVersionDiffResponse(
            workflowTypeId,
            fromVersionId,
            toVersionId,
            IsFirstVersion: false,
            StoredNarrative: null,
            narrativeGenerator.GenerateParts(diff),
            ToDto(diff));
    }

    internal static WorkflowTemplateDiffDto ToDto(WorkflowDiff diff)
    {
        return new WorkflowTemplateDiffDto(
            diff.IsEmpty,
            [.. diff.Sections.Select(section => new WorkflowTemplateSectionDiffDto(
                section.Name,
                section.Order,
                section.StableSectionId,
                ToContract(section.ChangeKind),
                section.IsLibraryLinked,
                ToDtos(section.FieldDiffs)))],
            [.. diff.Blocks.Select(block => new WorkflowTemplateBlockDiffDto(
                block.ReplacementTag,
                block.DisplayName,
                block.BlockType,
                block.StableBlockId,
                block.SectionName,
                ToContract(block.ChangeKind),
                block.IsLibraryLinked,
                ToDtos(block.FieldDiffs)))],
            [.. diff.Variables.Select(variable => new WorkflowTemplateVariableDiffDto(
                variable.ReplacementTag,
                variable.DisplayName,
                ToContract(variable.ChangeKind),
                ToDtos(variable.FieldDiffs)))],
            [.. diff.DocumentClasses.Select(documentClass => new WorkflowTemplateDocumentClassDiffDto(
                documentClass.Key,
                documentClass.DisplayName,
                ToContract(documentClass.ChangeKind),
                ToDtos(documentClass.FieldDiffs)))],
            ToDtos(diff.WorkflowFields));
    }

    private static WorkflowTemplateVersionDto ToDto(WorkflowVersionSummaryDto version)
    {
        return new WorkflowTemplateVersionDto(
            version.Id,
            version.ContentHash,
            version.ParentVersionId,
            version.CreatedAtUtc,
            version.CreatedByUserId,
            version.CreatedByName,
            version.PayloadTrimmedAt is null,
            version.PayloadTrimmedAt,
            version.Narrative);
    }

    private static List<WorkflowTemplateFieldDiffDto> ToDtos(IReadOnlyList<FieldDiff> fieldDiffs)
    {
        return [.. fieldDiffs.Select(field => new WorkflowTemplateFieldDiffDto(field.FieldName, field.Before, field.After))];
    }

    private static WorkflowTemplateChangeKind ToContract(DiffChangeKind changeKind)
    {
        return changeKind switch
        {
            DiffChangeKind.Added => WorkflowTemplateChangeKind.Added,
            DiffChangeKind.Removed => WorkflowTemplateChangeKind.Removed,
            DiffChangeKind.Modified => WorkflowTemplateChangeKind.Modified,
            DiffChangeKind.TypeChanged => WorkflowTemplateChangeKind.TypeChanged,
            _ => throw new ArgumentOutOfRangeException(nameof(changeKind), changeKind, "Unknown diff change kind."),
        };
    }
}

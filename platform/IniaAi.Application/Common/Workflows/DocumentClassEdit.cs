using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.Common.Workflows;

/// <param name="PageOverviews">The class's overview prompts; null leaves the stored prompts as they are, and prompts with no text clear them.</param>
public record DocumentClassEdit(
    string? OriginalKey,
    string Key,
    string DisplayName,
    string Description,
    string? DocumentCategoryId,
    DocumentClassRequirement Requirement,
    bool ShouldBeSingular,
    bool IsOutputDocument,
    string? LlmDescription = null,
    PageOverviewPrompts? PageOverviews = null);

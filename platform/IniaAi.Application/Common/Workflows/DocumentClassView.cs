using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Application.Common.Workflows;

public record DocumentClassView(
    string Key,
    string DisplayName,
    string Description,
    DocumentClassRequirement Requirement,
    bool ShouldBeSingular,
    bool IsOutputDocument,
    string? DocumentCategoryId,
    string? LlmDescription = null)
{
    public static DocumentClassView From(DocumentClass documentClass)
    {
        return new DocumentClassView(
            documentClass.Key,
            documentClass.DisplayName,
            documentClass.Description,
            documentClass.Requirement,
            documentClass.ShouldBeSingular,
            documentClass.IsOutputDocument,
            documentClass.DocumentCategoryId,
            documentClass.LlmDescription);
    }
}

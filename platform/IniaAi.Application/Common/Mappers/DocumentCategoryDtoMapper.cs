using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.WebApi.Contracts.Responses.DocumentCategories;

namespace IniaAi.Application.Mappers;

public static class DocumentCategoryDtoMapper
{
    public static DocumentCategoryDto ToDto(DocumentCategory category)
    {
        return new DocumentCategoryDto
        {
            Id = category.Id,
            Name = category.Name,
            DisplayOrder = category.DisplayOrder,
            CreatedAt = category.CreatedAt,
        };
    }
}

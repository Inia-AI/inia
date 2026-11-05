using IniaAi.Application.Mappers;
using IniaAi.Domain.Entities.Workflows.Templates;
using IniaAi.WebApi.Contracts.Responses.DocumentCategories;

namespace IniaAi.Application.UnitTests.Common.Mappers;

public class DocumentCategoryDtoMapperTests
{
    private static readonly DateTime _created = new(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ToDto_CarriesADocumentCategory()
    {
        DocumentCategoryDto documentCategory = DocumentCategoryDtoMapper.ToDto(new DocumentCategory
        {
            Id = "annual-report",
            Name = "Annual Report",
            DisplayOrder = 2,
            CreatedAt = _created,
        });

        Assert.Equal(("annual-report", "Annual Report", 2, _created), (documentCategory.Id, documentCategory.Name, documentCategory.DisplayOrder, documentCategory.CreatedAt));
    }
}

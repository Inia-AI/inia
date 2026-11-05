using IniaAi.Application.Commands.Files.UploadFile;
using IniaAi.Common.Enums;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Domain.Enums;
using IniaAi.WebApi.Contracts.Dto;

namespace IniaAi.Application.UnitTests.Commands.Files.UploadFile;

/// <summary>
/// The upload response says whether the platform reads the stored file into pages, so a caller
/// does not ask to process an email or a legacy Word file and get refused.
/// </summary>
public class FileDtoMapperTests
{
    [Theory]
    [InlineData("report.pdf", MediaType.ApplicationPdf, true)]
    [InlineData("newsletter.eml", MediaType.MessageRfc822, false)]
    [InlineData("memo.doc", MediaType.ApplicationMsWord, false)]
    public void TheUploadResponse_SaysWhetherTheFileIsReadIntoPages(string fileName, MediaType mediaType, bool producesPages)
    {
        var document = new Document(new FileMetadata("org-1", fileName, 1024, StorageProvider.Database, null), mediaType);

        FileDto dto = FileDtoMapper.ToDto(document);

        Assert.Equal(producesPages, dto.ProducesPages);
    }
}

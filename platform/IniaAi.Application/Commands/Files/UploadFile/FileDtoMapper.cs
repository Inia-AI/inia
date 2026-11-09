using IniaAi.Common.Extensions;
using IniaAi.Domain.Entities.Documents;
using IniaAi.WebApi.Contracts.Dto;
using ContractMediaType = IniaAi.WebApi.Contracts.Enums.MediaType;

namespace IniaAi.Application.Commands.Files.UploadFile;

public static class FileDtoMapper
{
    public static FileDto ToDto(Document document)
    {
        return new FileDto
        {
            Id = document.Id,
            Filename = document.Name,
            MediaType = Enum.Parse<ContractMediaType>(document.MediaType.ToString()),
            ProducesPages = document.MediaType.ProducesPages(),
            CountedPages = document.CountedPages,
            CountedAttachmentPages = document.CountedAttachmentPages,
        };
    }
}

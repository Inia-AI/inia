using IniaAi.Application.Commands.Counterparties;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.WebApi.Contracts.Responses.Counterparty;

namespace IniaAi.Application.Mappers;

public static class CounterpartyDtoMapper
{
    public static CounterpartyDto ToDto(Counterparty counterparty)
    {
        var dto = new CounterpartyDto
        {
            Id = counterparty.Id,
            ParentId = counterparty.ParentId,
            CounterpartyTypeIds = [.. counterparty.CounterpartyTypes.Select(type => type.Id)],
            Properties = counterparty.Properties is null ? null : new Dictionary<string, string>(counterparty.Properties),
        };
        CounterpartyFields.ReadText(counterparty, dto);
        return dto;
    }
}

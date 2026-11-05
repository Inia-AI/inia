using IniaAi.Domain.Entities.Counterparties;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using System.Linq.Expressions;

namespace IniaAi.Application.Queries.Counterparties;

public static class CounterpartyProjections
{
    public static readonly Expression<Func<Counterparty, CounterpartyListItemDto>> ToListItem = counterparty => new CounterpartyListItemDto
    {
        Id = counterparty.Id,
        ParentId = counterparty.ParentId,
        Name = counterparty.Name,
        NameVariants = counterparty.NameVariants,
        CounterpartyTypeIds = counterparty.CounterpartyTypes.Select(type => type.Id).ToList(),
    };
}

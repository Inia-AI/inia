using IniaAi.Common.Constants;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.UserData.Interfaces;
using IniaAi.WebApi.Contracts.Responses.CounterpartyTypes;
using Microsoft.EntityFrameworkCore;

namespace IniaAi.Application.Commands.CounterpartyTypes;

internal static class CounterpartyTypeRules
{
    public const string ID_REQUIRED = CounterpartyTerms.SINGULAR + " type ID is required";

    public static Task<bool> NameIsTakenAsync(IUserDataUnitOfWork unitOfWork, string name, string? exceptId, CancellationToken cancellationToken)
    {
        string lowered = name.ToLowerInvariant();

        return unitOfWork.Repository<CounterpartyType>().GetAll()
            .AnyAsync(t => t.Id != exceptId && t.Name.ToLower() == lowered, cancellationToken);
    }

    public static CounterpartyTypeListItemDto ToDto(CounterpartyType counterpartyType, int counterpartyCount, BuiltInCounterpartyTypes builtIn)
    {
        return new CounterpartyTypeListItemDto
        {
            Id = counterpartyType.Id,
            Name = counterpartyType.Name,
            Description = counterpartyType.Description,
            CounterpartyCount = counterpartyCount,
            IsBuiltIn = builtIn.Contains(counterpartyType.Name),
        };
    }
}

using IniaAi.WebApi.Contracts.Responses;
using IniaAi.WebApi.Contracts.Responses.Counterparty;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.Counterparty;

/// <summary>
/// Provides an example instance of <see cref="ODataPaginatedResponse{CounterpartyListItemDto}"/> for Swagger documentation.
/// </summary>
public class CounterpartyPageResultResponseExample : IExamplesProvider<ODataPaginatedResponse<CounterpartyListItemDto>>
{
    /// <summary>
    /// Returns an example of <see cref="ODataPaginatedResponse{CounterpartyListItemDto}"/> populated with sample data.
    /// </summary>
    /// <returns>An instance of <see cref="ODataPaginatedResponse{CounterpartyListItemDto}"/> with sample values.</returns>
    public ODataPaginatedResponse<CounterpartyListItemDto> GetExamples()
    {
        var counterparties = new List<CounterpartyListItemDto>
        {
            new()
            {
                Id = "counterparty-1",
                ParentId = null,
                CounterpartyTypeIds = ["type-001"],
                Name = "Acme Corporation",
                NameVariants = "Acme Corp|ACME"
            },
            new()
            {
                Id = "counterparty-2",
                ParentId = "counterparty-1",
                CounterpartyTypeIds = ["type-002"],
                Name = "Beta Holdings",
                NameVariants = "Beta Inc|B-Holdings"
            }
        };

        return new ODataPaginatedResponse<CounterpartyListItemDto>(
            counterparties,
            totalCount: counterparties.Count,
            skip: 0,
            top: 25
        );
    }
}

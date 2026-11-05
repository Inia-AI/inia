using IniaAi.WebApi.Contracts.Responses.Counterparty;
using IniaAi.WebApi.Swagger;

/// <summary>
/// Provides example data for CounterpartyDto used in Swagger documentation.
/// </summary>
public class CounterpartyDtoExample : IExamplesProvider<CounterpartyDto>
{
    /// <summary>
    /// Gets an example instance of <see cref="CounterpartyDto"/> for Swagger documentation.
    /// </summary>
    /// <returns>An example <see cref="CounterpartyDto"/> instance.</returns>
    public CounterpartyDto GetExamples()
    {
        return new()
        {
            Id = Guid.NewGuid().ToString(),
            Name = "Acme Capital Ltd.",
            CounterpartyTypeIds = ["Corporate"],
            NameVariants = "Acme Capital|Acme Investments",
            Website = "https://www.acmecapital.com"
        };
    }
}

using IniaAi.WebApi.Contracts.Requests.Counterparties;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.Counterparty;

/// <summary>
/// Provides an example instance of <see cref="CreateCounterpartyRequest"/> for Swagger documentation.
/// </summary>
public class CreateCounterpartyRequestExample : IExamplesProvider<CreateCounterpartyRequest>
{
    /// <summary>
    /// Gets an example instance of <see cref="CreateCounterpartyRequest"/> for Swagger documentation.
    /// </summary>
    /// <returns>An example <see cref="CreateCounterpartyRequest"/> instance.</returns>
    public CreateCounterpartyRequest GetExamples()
    {
        return new()
        {
            Name = "Acme Capital Ltd.",
            CounterpartyTypeIds = ["Corporate"],
            NameVariants = "Acme Capital|Acme Capital Ltd.",
        };
    }
}

using IniaAi.WebApi.Contracts.Requests.Counterparties;
using IniaAi.WebApi.Swagger;

namespace IniaAi.WebApi.ExampleProviders.Counterparty;

/// <summary>
/// Provides an example instance of <see cref="UpdateCounterpartyRequest"/> for Swagger documentation.
/// </summary>
public class UpdateCounterpartyRequestExample : IExamplesProvider<UpdateCounterpartyRequest>
{
    /// <summary>
    /// Gets an example instance of <see cref="UpdateCounterpartyRequest"/> for Swagger documentation.
    /// </summary>
    /// <returns>An example <see cref="UpdateCounterpartyRequest"/> instance.</returns>
    public UpdateCounterpartyRequest GetExamples()
    {
        return new()
        {
            Name = "Acme Capital Holdings",
            CounterpartyTypeIds = ["corporation"],
        };
    }
}

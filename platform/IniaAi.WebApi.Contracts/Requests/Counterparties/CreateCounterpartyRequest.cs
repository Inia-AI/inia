namespace IniaAi.WebApi.Contracts.Requests.Counterparties;

/// <summary>
/// Represents the request payload to create a new counterparty.
/// </summary>
public class CreateCounterpartyRequest : UpdateCounterpartyRequest
{
    /// <summary>
    /// The unique identifier of the counterparty.
    /// </summary>
    public string? Id { get; set; }
}

namespace IniaAi.Application.Commands.CounterpartyTypes;

/// <summary>
/// The fields a counterparty type is written with. The create and update commands carry them.
/// </summary>
public interface ICounterpartyTypeFields
{
    string Name { get; }

    string? Description { get; }
}

namespace IniaAi.Domain.Entities.Counterparties;

/// <summary>
/// A counterparty type the deployment seeds and protects: it cannot be deleted or renamed.
/// </summary>
/// <param name="Name">The stored type name code looks the type up by.</param>
/// <param name="Description">The description the seed gives a new type.</param>
public sealed record BuiltInCounterpartyType(string Name, string Description);

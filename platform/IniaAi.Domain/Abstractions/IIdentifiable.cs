namespace IniaAi.Domain.Abstractions;

/// <summary>
/// Interface for entities that have a unique identifier.
/// </summary>
public interface IIdentifiable
{
    /// <summary>
    /// The unique identifier for the entity.
    /// </summary>
    string Id { get; set; }
}
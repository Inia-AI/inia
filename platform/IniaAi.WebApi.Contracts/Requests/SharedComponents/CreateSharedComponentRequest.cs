using System.Text.Json;
using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Requests.SharedComponents;

/// <summary>
/// Creates a brand-new shared component with its first version from a definition authored in the
/// Library. The server re-normalizes the definition, validates any referenced Block components, and
/// stores it as the head of a fresh chain; there is no workflow anchor and nothing is fanned out.
/// </summary>
/// <param name="Kind">Whether the component is a single Block or a Section.</param>
/// <param name="Name">The display name; trimmed and capped at 250 characters.</param>
/// <param name="CategoryId">The curated category the component is filed under; required.</param>
/// <param name="Description">An optional human description.</param>
/// <param name="Definition">The definition JSON — a single block, or a section with inner blocks, matching the kind.</param>
public record CreateSharedComponentRequest(
    SharedComponentKind Kind,
    string Name,
    string CategoryId,
    string? Description,
    JsonElement Definition);

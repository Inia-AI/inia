using IniaAi.WebApi.Contracts.Enums;

namespace IniaAi.WebApi.Contracts.Responses.SharedComponents;

/// <summary>
/// The identity and head tokens of a freshly created shared component.
/// </summary>
/// <param name="Id">The new component identifier.</param>
/// <param name="Kind">Whether the component is a Block or a Section.</param>
/// <param name="Name">The stored display name.</param>
/// <param name="HeadVersionId">The identifier of the first (head) version.</param>
/// <param name="HeadContentHash">The content hash of the first version.</param>
public record CreateSharedComponentResponse(
    string Id,
    SharedComponentKind Kind,
    string Name,
    string HeadVersionId,
    string HeadContentHash);

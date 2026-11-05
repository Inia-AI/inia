using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.StoredKeys.DeleteStoredKey;

/// <summary>Deletes the organization's stored key. A block that names it fails until it is added again.</summary>
public record DeleteStoredKeyCommand(
    string OrganizationId,
    string UserId,
    string Name) : IRequest<ErrorOr<Deleted>>;

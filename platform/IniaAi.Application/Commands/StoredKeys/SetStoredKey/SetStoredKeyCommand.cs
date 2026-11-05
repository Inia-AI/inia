using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Commands.StoredKeys.SetStoredKey;

/// <summary>Adds the organization's stored key by this name, or replaces its value.</summary>
public record SetStoredKeyCommand(
    string OrganizationId,
    string UserId,
    string Name,
    string Value) : IRequest<ErrorOr<Success>>;

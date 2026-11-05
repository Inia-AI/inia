using ErrorOr;
using IniaAi.Application.Common.Dispatching;

namespace IniaAi.Application.Queries.StoredKeys.GetStoredKeys;

/// <summary>The names of the organization's stored keys, by name. Never their values.</summary>
public record GetStoredKeysQuery(
    string OrganizationId,
    string UserId) : IRequest<ErrorOr<IReadOnlyList<StoredKeySummary>>>;

public record StoredKeySummary(string Name, DateTime ChangedAt);

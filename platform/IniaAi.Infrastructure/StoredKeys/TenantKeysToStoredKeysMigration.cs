using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Domain.Entities.StoredKeys;
using IniaAi.Domain.Entities.UserManagement;
using IniaAi.Domain.StoredKeys;
using IniaAi.Infrastructure.Persistence.Core.Contexts;
using IniaAi.Infrastructure.Persistence.UserData.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.StoredKeys;

/// <summary>
/// Moves each tenant's Parallel, Perplexity and FMP API key into a stored key of the same name, which
/// <c>{Organization.ParallelApiKey}</c> and the like now name, and clears the tenant field. A stored
/// key that already has the name keeps its value, so an admin's later edit is never overwritten.
/// The stored keys save before the fields clear, so a failure in between leaves the fields for the
/// next start, which finds the stored keys and only clears them.
/// </summary>
public sealed class TenantKeysToStoredKeysMigration : IStartupDataMigration
{
    private static readonly (string Name, Func<Organization, string?> Read, Action<Organization> Clear)[] _tenantKeys =
    [
        (StoredKeyPlaceholders.FormerTenantKeyNames[0], organization => organization.ParallelApiKey, organization => organization.ParallelApiKey = null),
        (StoredKeyPlaceholders.FormerTenantKeyNames[1], organization => organization.PerplexityApiKey, organization => organization.PerplexityApiKey = null),
        (StoredKeyPlaceholders.FormerTenantKeyNames[2], organization => organization.FmpApiKey, organization => organization.FmpApiKey = null),
    ];

    private readonly UserDataDbContext _userData;
    private readonly CoreDbContext _core;
    private readonly IStoredKeyCipher _cipher;
    private readonly ILogger<TenantKeysToStoredKeysMigration> _logger;

    public TenantKeysToStoredKeysMigration(
        UserDataDbContext userData,
        CoreDbContext core,
        IStoredKeyCipher cipher,
        ILogger<TenantKeysToStoredKeysMigration> logger)
    {
        _userData = userData;
        _core = core;
        _cipher = cipher;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        List<Organization> organizations = await _userData.Organizations
            .Where(organization => organization.ParallelApiKey != null
                || organization.PerplexityApiKey != null
                || organization.FmpApiKey != null)
            .ToListAsync(cancellationToken);

        if (organizations.Count == 0)
        {
            return;
        }

        List<string> organizationIds = [.. organizations.Select(organization => organization.Id)];
        var existing = await _core.OrganizationStoredKeys
            .Where(key => organizationIds.Contains(key.OrganizationId))
            .Select(key => new { key.OrganizationId, key.Name })
            .ToListAsync(cancellationToken);
        HashSet<(string OrganizationId, string Name)> stored = [.. existing.Select(key => (key.OrganizationId, key.Name))];

        int moved = 0;

        foreach (Organization organization in organizations)
        {
            foreach ((string name, Func<Organization, string?> read, Action<Organization> clear) in _tenantKeys)
            {
                string? value = read(organization)?.Trim();

                if (!string.IsNullOrEmpty(value) && stored.Add((organization.Id, name)))
                {
                    _ = _core.OrganizationStoredKeys.Add(new OrganizationStoredKey
                    {
                        OrganizationId = organization.Id,
                        Name = name,
                        ProtectedValue = _cipher.Protect(value),
                    });
                    moved++;
                }

                clear(organization);
            }
        }

        _ = await _core.SaveChangesAsync(cancellationToken);
        _ = await _userData.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Moved {Count} tenant API keys of {Organizations} organizations into stored keys",
            moved,
            organizations.Count);
    }
}

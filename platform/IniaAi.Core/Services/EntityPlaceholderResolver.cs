using System.Collections.Concurrent;
using System.Reflection;
using IniaAi.Application.Common.Interfaces.Contexts;
using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Common.Reflection;
using IniaAi.Core.Services.Abstraction;
using IniaAi.Domain.Entities.Counterparties;
using IniaAi.Domain.Entities.UserManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IniaAi.Core.Services;

/// <summary>
/// Resolves entity-based placeholders by looking up Counterparty, PeerCompanies, and Organization
/// from the database with caching support.
/// </summary>
public class EntityPlaceholderResolver : IEntityPlaceholderResolver
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly ILogger<EntityPlaceholderResolver> _logger;

    private readonly ConcurrentDictionary<string, Counterparty> _counterpartyCache = [];
    private readonly ConcurrentDictionary<string, Counterparty?> _counterpartyByNameCache = [];
    private readonly ConcurrentDictionary<string, Organization> _organizationCache = [];

    private static readonly Dictionary<string, PropertyInfo> _counterpartyStringColumns =
        typeof(Counterparty)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(string))
            .ToDictionary(property => property.Name, StringComparer.OrdinalIgnoreCase);

    public EntityPlaceholderResolver(
        IServiceScopeFactory serviceScopeFactory,
        ILogger<EntityPlaceholderResolver> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string? ResolveCounterparty(string? counterpartyId, string? key)
    {
        if (string.IsNullOrEmpty(counterpartyId))
        {
            return null;
        }

        return ResolveRawValue(GetCounterpartyFromCacheOrDatabase(counterpartyId), key);
    }

    /// <inheritdoc/>
    public string? ResolveCounterpartyByName(
        string? organizationId,
        string? counterpartyName,
        string? key)
    {
        if (string.IsNullOrEmpty(organizationId)
            || string.IsNullOrWhiteSpace(counterpartyName))
        {
            return null;
        }

        return ResolveRawValue(
            GetCounterpartyByNameFromCacheOrDatabase(organizationId, counterpartyName),
            key);
    }

    /// <inheritdoc/>
    public string? ResolvePeerCompanies(List<string>? peerCompanyIds, string[] parts)
    {
        if (parts.Length == 0)
        {
            return null;
        }

        if (peerCompanyIds is null || peerCompanyIds.Count == 0)
        {
            return null;
        }

        string key = parts[0];
        string property = parts.Length > 1 ? parts[1] : "Name";

        string[] tokens = key.Split(['[', ']'], StringSplitOptions.RemoveEmptyEntries);
        bool isArrayAccess = key.Contains("[]");

        // Handle PeerCompanies[].Property
        if (isArrayAccess)
        {
            List<string> values = [];
            foreach (string peerCompanyId in peerCompanyIds)
            {
                Counterparty? peerCompany = GetCounterpartyFromCacheOrDatabase(peerCompanyId);

                if (peerCompany is null)
                {
                    continue;
                }

                string? value = ReflectionUtils.GetPropertyValue(peerCompany, property);

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                values.Add(value);
            }

            return string.Join(", ", values);
        }

        // Handle PeerCompanies[1].Property (1-based)
        if (tokens.Length == 2 && int.TryParse(tokens[1], out int oneBasedIndex))
        {
            int adjustedIndex = oneBasedIndex - 1;
            if (adjustedIndex < 0 || adjustedIndex >= peerCompanyIds.Count)
            {
                return null;
            }

            string peerCompanyId = peerCompanyIds[adjustedIndex];
            Counterparty? peerCompany = GetCounterpartyFromCacheOrDatabase(peerCompanyId);
            return peerCompany is not null
                ? ReflectionUtils.GetPropertyValue(peerCompany, property)
                : null;
        }

        // Fallback when no index: return a list of results for all PeerCompanies
        string listOfNames = string.Empty;
        foreach (string peerCompanyId in peerCompanyIds)
        {
            Counterparty? peerCompany = GetCounterpartyFromCacheOrDatabase(peerCompanyId);

            if (peerCompany is null)
            {
                continue;
            }

            string? value = ReflectionUtils.GetPropertyValue(peerCompany, property);
            if (!string.IsNullOrWhiteSpace(value))
            {
                listOfNames += $"{value}\n";
            }
        }

        return string.IsNullOrWhiteSpace(listOfNames)
            ? null
            : listOfNames.Trim('\n');
    }

    /// <inheritdoc/>
    public string? ResolveOrganization(string? organizationId, string property)
    {
        if (string.IsNullOrEmpty(organizationId))
        {
            return null;
        }

        Organization? organization = GetOrganizationFromCacheOrDatabase(organizationId);
        if (organization is not null)
        {
            return ReflectionUtils.GetPropertyValue(organization, property);
        }

        return null;
    }

    /// <inheritdoc/>
    public void InvalidateCounterparty(string counterpartyId)
    {
        if (string.IsNullOrWhiteSpace(counterpartyId))
        {
            return;
        }

        foreach ((string cacheKey, Counterparty? cachedCounterparty) in _counterpartyByNameCache)
        {
            if (cachedCounterparty?.Id == counterpartyId)
            {
                _ = _counterpartyByNameCache.TryRemove(cacheKey, out _);
            }
        }

        if (_counterpartyCache.TryRemove(counterpartyId, out _))
        {
            _logger.LogInformation("Removed counterparty {CounterpartyId} from cache.", counterpartyId);
        }
        else
        {
            _logger.LogWarning("Attempted to remove non-existent counterparty {CounterpartyId} from cache.", counterpartyId);
        }
    }

    /// <inheritdoc/>
    public Counterparty? GetCounterparty(string? counterpartyId)
    {
        return GetCounterpartyFromCacheOrDatabase(counterpartyId);
    }

    /// <summary>
    /// Resolves the raw value a placeholder's first segment names. A real string column on the
    /// counterparty wins; otherwise the segment is a key into the freeform <see cref="Counterparty.Properties"/>
    /// bag, falling back to the parent's bag when the fund has no value of its own. A blank key reads
    /// the name. The value is returned verbatim — any JSON-path or tag traversal on a deeper segment is
    /// the caller's job, exactly as it is for a block response.
    /// </summary>
    private static string? ResolveRawValue(Counterparty? counterparty, string? key)
    {
        if (counterparty is null)
        {
            return null;
        }

        if (string.IsNullOrEmpty(key))
        {
            return counterparty.Name;
        }

        if (TryGetStringColumn(counterparty, key, out string? columnValue))
        {
            return columnValue;
        }

        string? bagValue = LookupBagValue(counterparty, key);

        return bagValue is null && counterparty.Parent is not null
            ? LookupBagValue(counterparty.Parent, key)
            : bagValue;
    }

    /// <summary>
    /// A first segment naming a string column (<c>Name</c>, <c>Email</c>, ...) resolves to that column,
    /// shadowing any bag key of the same name. Non-string members (navigations, collections, the bag
    /// itself) are not columns, so they fall through to a bag lookup and can never leak a related row.
    /// </summary>
    private static bool TryGetStringColumn(Counterparty counterparty, string key, out string? value)
    {
        if (_counterpartyStringColumns.TryGetValue(key, out PropertyInfo? property))
        {
            value = property.GetValue(counterparty) as string;

            return true;
        }

        value = null;

        return false;
    }

    private static string? LookupBagValue(Counterparty counterparty, string key)
    {
        // Properties is a case-insensitive map (CounterpartyPropertyBag), so this is a plain lookup.
        return counterparty.Properties?.GetValueOrDefault(key);
    }

    private Counterparty? GetCounterpartyFromCacheOrDatabase(string? counterpartyId)
    {
        if (string.IsNullOrEmpty(counterpartyId))
        {
            return null;
        }

        if (_counterpartyCache.TryGetValue(counterpartyId, out Counterparty? cachedCounterparty))
        {
            return cachedCounterparty;
        }

        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        IUserDataDbContextAccessor userDataDbContextFactory = scope.ServiceProvider
                  .GetRequiredService<IUserDataDbContextAccessor>();
        IUserDataDbContext db = userDataDbContextFactory.CreateDbContext();

        Counterparty? result = db.Counterparties
            .Include(c => c.Parent)
            .SingleOrDefault(c => c.Id == counterpartyId);

        if (result is not null)
        {
            _ = _counterpartyCache.TryAdd(counterpartyId, result);
        }

        return result;
    }

    private static bool IsNamed(Counterparty counterparty, string normalizedName)
    {
        return counterparty.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)
            || counterparty.NameVariants.Trim().Equals(normalizedName, StringComparison.OrdinalIgnoreCase)
            || CounterpartyNameRules.SplitNameVariants(counterparty.NameVariants)
                .Any(variant => variant.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));
    }

    private Counterparty? GetCounterpartyByNameFromCacheOrDatabase(
        string organizationId,
        string counterpartyName)
    {
        string normalizedName = counterpartyName.Trim().ToLowerInvariant();
        string cacheKey = $"{organizationId}|{normalizedName}";

        if (_counterpartyByNameCache.TryGetValue(cacheKey, out Counterparty? cachedCounterparty))
        {
            return cachedCounterparty;
        }

        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        IUserDataDbContextAccessor userDataDbContextFactory = scope.ServiceProvider
                  .GetRequiredService<IUserDataDbContextAccessor>();
        IUserDataDbContext db = userDataDbContextFactory.CreateDbContext();

        Counterparty? result = db.Counterparties
            .Include(c => c.Parent)
            .Where(c => c.OrganizationId == organizationId
                && (c.Name.ToLower() == normalizedName || c.NameVariants.ToLower().Contains(normalizedName)))
            .AsEnumerable()
            .Where(c => IsNamed(c, normalizedName))
            .OrderBy(c => c.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(c => c.Id, StringComparer.Ordinal)
            .FirstOrDefault();

        if (result is null)
        {
            _logger.LogInformation(
                "No counterparty named '{CounterpartyName}' found in organization {OrganizationId}.",
                counterpartyName,
                organizationId);
        }

        _counterpartyByNameCache[cacheKey] = result;

        return result;
    }

    private Organization? GetOrganizationFromCacheOrDatabase(string? organizationId)
    {
        if (string.IsNullOrEmpty(organizationId))
        {
            return null;
        }

        if (_organizationCache.TryGetValue(organizationId, out Organization? cachedOrganization))
        {
            return cachedOrganization;
        }

        using IServiceScope scope = _serviceScopeFactory.CreateScope();
        IUserDataDbContextAccessor userDataDbContextFactory = scope.ServiceProvider
                  .GetRequiredService<IUserDataDbContextAccessor>();
        IUserDataDbContext db = userDataDbContextFactory.CreateDbContext();

        Organization? result = db.Organizations.SingleOrDefault(o => o.Id == organizationId);

        if (result is not null)
        {
            _ = _organizationCache.TryAdd(organizationId, result);
        }

        return result;
    }
}

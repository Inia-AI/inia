using IniaAi.Application.Common.Interfaces.Services;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.TokenUsage;
using IniaAi.Domain.Entities.TokenUsage.Enums;
using IniaAi.Domain.UserData.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace IniaAi.Infrastructure.Services;

/// <summary>
/// Provides model resolution for different usage contexts based on organization settings.
/// Uses caching to minimize database calls for frequently accessed settings.
/// </summary>
public class OrganizationModelSettingsService : IOrganizationModelSettingsService
{
    private readonly IUserDataUnitOfWork _userDataUnitOfWork;
    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IMemoryCache _cache;
    private readonly ILogger<OrganizationModelSettingsService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="OrganizationModelSettingsService"/> class.
    /// </summary>
    /// <param name="userDataUnitOfWork">The user data unit of work.</param>
    /// <param name="coreUnitOfWork">The core unit of work.</param>
    /// <param name="cache">The memory cache.</param>
    /// <param name="logger">The logger.</param>
    public OrganizationModelSettingsService(
        IUserDataUnitOfWork userDataUnitOfWork,
        ICoreUnitOfWork coreUnitOfWork,
        IMemoryCache cache,
        ILogger<OrganizationModelSettingsService> logger)
    {
        _userDataUnitOfWork = userDataUnitOfWork;
        _coreUnitOfWork = coreUnitOfWork;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<ModelPricing?> GetModelForContextAsync(
        string? organizationId,
        UsageContext context,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = $"ModelSettings:{organizationId ?? "global"}:{context}";

        if (_cache.TryGetValue(cacheKey, out ModelPricing? cachedResult))
        {
            return cachedResult;
        }

        OrganizationModelSettings? settings = await GetSettingsAsync(organizationId, context, cancellationToken);

        if (settings is not null)
        {
            ModelPricing? modelPricing = await _coreUnitOfWork.Repository<ModelPricing>()
                .SingleOrDefaultAsync(
                    m => m.Id == settings.ModelPricingId,
                    asNoTracking: true,
                    cancellationToken: cancellationToken);

            _ = _cache.Set(cacheKey, modelPricing, CacheDuration);
            return modelPricing;
        }

        // Cache null result to avoid repeated DB lookups
        _ = _cache.Set(cacheKey, (ModelPricing?)null, CacheDuration);
        return null;
    }

    /// <inheritdoc/>
    public async Task<string?> GetModelIdForContextAsync(
        string? organizationId,
        UsageContext context,
        CancellationToken cancellationToken = default)
    {
        string cacheKey = $"ModelSettingsId:{organizationId ?? "global"}:{context}";

        if (_cache.TryGetValue(cacheKey, out string? cachedResult))
        {
            return cachedResult;
        }

        OrganizationModelSettings? settings = await GetSettingsAsync(organizationId, context, cancellationToken);
        string? modelPricingId = settings?.ModelPricingId;

        // Cache the result (including null)
        _ = _cache.Set(cacheKey, modelPricingId, CacheDuration);

        return modelPricingId;
    }

    private async Task<OrganizationModelSettings?> GetSettingsAsync(
        string? organizationId,
        UsageContext context,
        CancellationToken cancellationToken)
    {
        // 1. Try organization-specific setting
        if (organizationId is not null)
        {
            OrganizationModelSettings? orgSettings = await _userDataUnitOfWork.Repository<OrganizationModelSettings>()
                .GetAll()
                .Where(x => x.OrganizationId == organizationId
                           && x.UsageContext == context
                           && x.IsEnabled)
                .OrderByDescending(x => x.Priority)
                .FirstOrDefaultAsync(cancellationToken);

            if (orgSettings is not null)
            {
                _logger.LogDebug(
                    "Found organization-specific model setting for org {OrganizationId}, context {Context}: {ModelId}",
                    organizationId, context, orgSettings.ModelPricingId);
                return orgSettings;
            }
        }

        // 2. Try global setting (OrganizationId = null)
        OrganizationModelSettings? globalSettings = await _userDataUnitOfWork.Repository<OrganizationModelSettings>()
            .GetAll()
            .Where(x => x.OrganizationId == null
                       && x.UsageContext == context
                       && x.IsEnabled)
            .OrderByDescending(x => x.Priority)
            .FirstOrDefaultAsync(cancellationToken);

        if (globalSettings is not null)
        {
            _logger.LogDebug(
                "Found global model setting for context {Context}: {ModelId}",
                context, globalSettings.ModelPricingId);
            return globalSettings;
        }

        _logger.LogDebug(
            "No model setting found for org {OrganizationId}, context {Context}",
            organizationId, context);

        return null;
    }
}

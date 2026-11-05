using IniaAi.Common.Interfaces.Persistence;
using IniaAi.Core.Workflows.StoredKeys;
using IniaAi.Domain.StoredKeys;
using Microsoft.Extensions.DependencyInjection;

namespace IniaAi.Infrastructure.StoredKeys;

public static class StoredKeysServices
{
    /// <summary>Needs the host's Data Protection registration.</summary>
    public static IServiceCollection AddStoredKeys(this IServiceCollection services)
    {
        return services
            .AddSingleton<IStoredKeyCipher, DataProtectionStoredKeyCipher>()
            .AddScoped<IStoredKeyResolver, StoredKeyResolver>()
            .AddScoped<IStartupDataMigration, TenantKeysToStoredKeysMigration>();
    }
}

using IniaAi.Configuration;
using IniaAi.Configuration.Platform;
using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.StoredKeys;
using IniaAi.Domain.StoredKeys;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Security.Cryptography;

namespace IniaAi.Core.Workflows.StoredKeys;

public sealed class StoredKeyResolver : IStoredKeyResolver
{
    private const string UNREADABLE =
        "The stored key '{0}' cannot be decrypted with this deployment's key ring. An admin sets its value again under Administration, Stored keys.";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IStoredKeyCipher _cipher;
    private readonly StoredKeySettings _storedKeySettings;

    public StoredKeyResolver(ICoreUnitOfWork coreUnitOfWork, IStoredKeyCipher cipher, IOptions<PlatformSettings> platformSettings)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _cipher = cipher;
        _storedKeySettings = platformSettings.Value.StoredKeys;
    }

    public async Task<string?> ResolveAsync(string organizationId, string name, CancellationToken cancellationToken)
    {
        string? protectedValue = await _coreUnitOfWork.Repository<OrganizationStoredKey>()
            .GetAll()
            .AsNoTracking()
            .Where(key => key.OrganizationId == organizationId && key.Name == name)
            .Select(key => key.ProtectedValue)
            .FirstOrDefaultAsync(cancellationToken);

        if (protectedValue is null)
        {
            return _storedKeySettings.DeploymentValue(name);
        }

        try
        {
            return _cipher.Unprotect(protectedValue);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, UNREADABLE, name), exception);
        }
    }
}

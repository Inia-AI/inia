using IniaAi.Domain.Core.Interfaces;
using IniaAi.Domain.Entities.StoredKeys;
using IniaAi.Domain.StoredKeys;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;

namespace IniaAi.Core.Workflows.StoredKeys;

public sealed class StoredKeyResolver : IStoredKeyResolver
{
    private const string UNREADABLE =
        "The stored key '{0}' cannot be decrypted with this deployment's key ring. An admin sets its value again under Administration, Stored keys.";

    private readonly ICoreUnitOfWork _coreUnitOfWork;
    private readonly IStoredKeyCipher _cipher;

    public StoredKeyResolver(ICoreUnitOfWork coreUnitOfWork, IStoredKeyCipher cipher)
    {
        _coreUnitOfWork = coreUnitOfWork;
        _cipher = cipher;
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
            return null;
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

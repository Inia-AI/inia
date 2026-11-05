using IniaAi.Domain.StoredKeys;
using Microsoft.AspNetCore.DataProtection;

namespace IniaAi.Infrastructure.StoredKeys;

/// <summary>
/// Encrypts with the app's Data Protection key ring. Both apps share that ring, so a key the admin
/// app stores decrypts in the API that runs the workflow.
/// </summary>
public sealed class DataProtectionStoredKeyCipher : IStoredKeyCipher
{
    private const string PURPOSE = "IniaAi.StoredKeys.v1";

    private readonly IDataProtector _protector;

    public DataProtectionStoredKeyCipher(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(PURPOSE);
    }

    public string Protect(string value)
    {
        return _protector.Protect(value);
    }

    public string Unprotect(string protectedValue)
    {
        return _protector.Unprotect(protectedValue);
    }
}

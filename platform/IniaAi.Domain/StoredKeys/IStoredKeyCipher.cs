namespace IniaAi.Domain.StoredKeys;

/// <summary>Encrypts a stored key for the database and decrypts it for the request that uses it.</summary>
public interface IStoredKeyCipher
{
    string Protect(string value);

    string Unprotect(string protectedValue);
}

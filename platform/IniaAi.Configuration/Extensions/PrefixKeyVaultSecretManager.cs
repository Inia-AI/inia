using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;

namespace IniaAi.Configuration.Extensions;

/// <summary>
/// Loads only the Key Vault secrets under one scope prefix (e.g. <c>shared--*</c> or <c>api--*</c>),
/// strips the prefix, and maps the remaining <c>--</c> separators to <c>:</c> config keys.
/// Register one manager per scope — shared first, service-specific second — so the
/// service-specific source overrides shared values, mirroring the AWS SSM inheritance pattern.
/// </summary>
public sealed class PrefixKeyVaultSecretManager : KeyVaultSecretManager
{
    private const string SEPARATOR = "--";

    private readonly string _prefix;

    public PrefixKeyVaultSecretManager(string scope)
    {
        _prefix = $"{scope}{SEPARATOR}";
    }

    public override bool Load(SecretProperties properties)
    {
        return properties.Name.Length > _prefix.Length
            && properties.Name.StartsWith(_prefix, StringComparison.OrdinalIgnoreCase);
    }

    public override string GetKey(KeyVaultSecret secret)
    {
        return secret.Name[_prefix.Length..]
            .Replace(SEPARATOR, ConfigurationPath.KeyDelimiter, StringComparison.Ordinal);
    }
}

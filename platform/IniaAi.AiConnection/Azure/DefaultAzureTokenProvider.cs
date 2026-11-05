using Azure.Core;
using Azure.Identity;

namespace IniaAi.AiConnection.Azure;

/// <summary>
/// Acquires Entra ID tokens for Azure Cognitive Services using DefaultAzureCredential.
/// Thread-safe — the SDK caches tokens internally and refreshes ~5 min before expiry.
/// </summary>
public class DefaultAzureTokenProvider : IAzureTokenProvider
{
    private static readonly string[] Scopes = ["https://cognitiveservices.azure.com/.default"];

    private readonly TokenCredential _credential;

    public DefaultAzureTokenProvider()
    {
        _credential = new DefaultAzureCredential();
    }

    internal DefaultAzureTokenProvider(TokenCredential credential)
    {
        _credential = credential;
    }

    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        AccessToken token = await _credential.GetTokenAsync(
            new TokenRequestContext(Scopes),
            cancellationToken);

        return token.Token;
    }
}

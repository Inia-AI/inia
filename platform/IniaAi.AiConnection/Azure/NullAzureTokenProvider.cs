namespace IniaAi.AiConnection.Azure;

/// <summary>
/// No-op token provider for API key authentication mode.
/// Throws if accidentally called — avoids null checks in connector code.
/// </summary>
public class NullAzureTokenProvider : IAzureTokenProvider
{
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "Token provider should not be called in API key authentication mode.");
    }
}

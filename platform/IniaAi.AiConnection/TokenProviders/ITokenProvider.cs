namespace IniaAi.AiConnection.TokenProviders;

public interface ITokenProvider
{
    Task<string> GetTokenAsync(CancellationToken cancellationToken = default);
}

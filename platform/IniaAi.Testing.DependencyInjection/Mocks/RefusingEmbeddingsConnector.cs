using IniaAi.AiConnection.Abstractions;
using IniaAi.Testing.XunitAttributes;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// What <see cref="TestEmbeddingsConnectorFactory"/> hands out when <see cref="PaidApi"/> forbids
/// paid calls. A call that reaches it throws an error that names the opt-in, instead of spending money.
/// </summary>
public sealed class RefusingEmbeddingsConnector : IEmbeddingsConnector
{
    public string ModelName => nameof(RefusingEmbeddingsConnector);

    public string ModelId => nameof(RefusingEmbeddingsConnector);

    public int TotalTokens => 0;

    public Task<List<double[]>> GetResponseAsync(string document, CancellationToken cancellationToken = default)
    {
        throw Refusal();
    }

    public Task<List<double[]>> GetResponseAsync(List<string> documents, CancellationToken cancellationToken = default)
    {
        throw Refusal();
    }

    private static InvalidOperationException Refusal()
    {
        return new InvalidOperationException(
            "A test called the real embeddings model, and local test runs make no paid calls. " +
            $"Give the test a {nameof(WordHashEmbeddingsConnector)}, or mark it [PaidApiFact] and run it with {PaidApi.OPT_IN_VARIABLE}=1.");
    }
}

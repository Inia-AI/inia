using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Extensions;
using IniaAi.Configuration.Enums.Cohere;
using System.Text.RegularExpressions;

namespace IniaAi.Testing.DependencyInjection.Mocks;

/// <summary>
/// Stands in for the embeddings model in tests of the code around it, so they need no API key and
/// spend nothing. A text's vector counts its words, each hashed to a fixed slot, so texts that
/// share words score as similar and every run gets the same vectors. It reports the production
/// model's name and id, so the code under test takes its production path. A test whose assertion
/// is the model's own sense of meaning keeps the real connector from
/// <see cref="TestEmbeddingsConnectorFactory"/>.
/// </summary>
public sealed partial class WordHashEmbeddingsConnector : IEmbeddingsConnector
{
    private const AiModel MODEL = AiModel.EmbedV40;
    private const int DIMENSIONS = 1024;
    private const uint FNV_OFFSET_BASIS = 2166136261;
    private const uint FNV_PRIME = 16777619;

    private int _totalTokens;

    public string ModelName => $"Cohere: {MODEL}";

    public string ModelId => MODEL.GetEnumMember();

    public int TotalTokens => _totalTokens;

    public Task<List<double[]>> GetResponseAsync(string document, CancellationToken cancellationToken = default)
    {
        return GetResponseAsync([document], cancellationToken);
    }

    public Task<List<double[]>> GetResponseAsync(List<string> documents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(documents.Select(Embed).ToList());
    }

    private double[] Embed(string text)
    {
        string[] words = [.. WordPattern().Matches(text.ToLowerInvariant()).Select(match => match.Value)];
        if (words.Length == 0)
        {
            words = [string.Empty];
        }

        _ = Interlocked.Add(ref _totalTokens, words.Length);

        var vector = new double[DIMENSIONS];
        foreach (string word in words)
        {
            vector[Slot(word)] += 1;
        }

        double length = Math.Sqrt(vector.Sum(value => value * value));
        for (int i = 0; i < DIMENSIONS; i++)
        {
            vector[i] /= length;
        }

        return vector;
    }

    private static int Slot(string word)
    {
        uint hash = FNV_OFFSET_BASIS;
        foreach (char character in word)
        {
            hash = (hash ^ character) * FNV_PRIME;
        }

        return (int)(hash % DIMENSIONS);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex WordPattern();
}

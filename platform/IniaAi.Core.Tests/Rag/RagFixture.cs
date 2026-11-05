using IniaAi.AiConnection.Abstractions;
using IniaAi.Common.Enums;
using IniaAi.Core.Rag;
using IniaAi.Domain.Entities.Documents;
using IniaAi.Testing;
using IniaAi.Testing.DependencyInjection;
using IniaAi.Testing.DependencyInjection.Mocks;

namespace IniaAi.Core.Tests.Rag;

public class RagFixture<T> : TestsBaseFixture<T>
{
    private List<Document>? _documents = null;

    public RagService GetRagService(ITestOutputHelper testOutputHelper, IChatConnector chatConnector)
    {
        return new RagService(
            TestsLogger.CreateLogger<RagService>(testOutputHelper),
            new WordHashEmbeddingsConnector(),
            chatConnector,
            new ServiceScopeFactory());
    }

    internal async Task<List<Document>> GetDocumentsAsync()
    {
        if (_documents is not null)
        {
            return _documents;
        }

        _documents = [];

        var doc1 = new Document(new FileMetadata("1234", "Document 1", [0]), MediaType.Unknown);
        var doc2 = new Document(new FileMetadata("1234", "Document 2", [0]), MediaType.Unknown);

        // Simulating pages for two documents with different order
        int[] doc1Pages = [10, 7, 3, 9, 1, 2, 5, 8, 6, 4];
        int[] doc2Pages = [4, 6, 8, 5, 2, 1, 9, 3, 7, 10];

        foreach (int i in doc1Pages)
        {
            _ = new Page(doc1, i, i) { Text = $"1. Page {i} Text" };
        }

        foreach (int i in doc2Pages)
        {
            _ = new Page(doc2, i, i) { Text = $"2. Page {i} Text" };
        }

        _documents = [doc1, doc2];

        var embeddingsConnector = new WordHashEmbeddingsConnector();

        foreach (Document document in _documents)
        {
            var pageTexts = document.Pages.OrderBy(p => p.PageNumber).Select(p => p.Text).ToList();
            List<double[]> vectors = await embeddingsConnector.GetResponseAsync(pageTexts!);

            for (int i = 0; i < document.Pages.Count; i++)
            {
                Page page = document[i + 1]
                    ?? throw new InvalidOperationException("Page should exist");

                page.EmbeddingVector = new(embeddingsConnector.ModelName, vectors[i]);
            }
        }

        return _documents;
    }
}

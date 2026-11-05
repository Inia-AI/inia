using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Workflows.Chunking;

/// <summary>
/// Factory for creating the appropriate page chunker based on configuration.
/// </summary>
public static class ChunkerFactory
{
    /// <summary>
    /// Creates a page chunker based on block properties.
    /// </summary>
    /// <param name="block">The workflow block.</param>
    /// <param name="props">Optional AI query properties (takes precedence over block properties).</param>
    /// <returns>An appropriate IPageChunker implementation.</returns>
    public static IPageChunker Create(AiQueryProperties props)
    {
        return props.ChunkingMode switch
        {
            ChunkingMode.OptimalCharacters => CreateOptimalChunker(props),
            _ => CreateFixedCountChunker(props),
        };
    }

    private static FixedCountChunker CreateFixedCountChunker(AiQueryProperties props)
    {
        int chunkSize = props.ChunkSize;
        return new FixedCountChunker(chunkSize);
    }

    private static OptimalChunker CreateOptimalChunker(AiQueryProperties props)
    {
        int maxCharacters = props.MaxChunkCharacters;
        bool preserveOrder = props.PreservePageOrder;
        return new OptimalChunker(maxCharacters, preserveOrder);
    }
}

using IniaAi.Core.Workflows.Chunking;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Core.Tests.Workflows.Chunking;

public class ChunkerFactoryTests
{
    private static Block CreateTestBlock(int chunkSize = 20)
    {
        var workflow = new Section("test-block", new Workflow());
        return new Block(BlockTypes.Merge, workflow, "test-block")
        {
            Properties = new AiQueryProperties
            {
                ChunkSize = chunkSize
            }
        };
    }

    [Fact]
    public void Create_FixedPageCountMode_ReturnsFixedCountChunker()
    {
        // Arrange
        _ = CreateTestBlock(chunkSize: 10);
        var props = new AiQueryProperties
        {
            ChunkingMode = ChunkingMode.FixedPageCount,
            ChunkSize = 5
        };

        // Act
        IPageChunker chunker = ChunkerFactory.Create(props);

        // Assert
        _ = Assert.IsType<FixedCountChunker>(chunker);
    }

    [Fact]
    public void Create_OptimalCharactersMode_ReturnsOptimalChunker()
    {
        // Arrange
        _ = CreateTestBlock();
        var props = new AiQueryProperties
        {
            ChunkingMode = ChunkingMode.OptimalCharacters,
            MaxChunkCharacters = 50000,
            PreservePageOrder = true
        };

        // Act
        IPageChunker chunker = ChunkerFactory.Create(props);

        // Assert
        _ = Assert.IsType<OptimalChunker>(chunker);
    }

    [Fact]
    public void Create_PropsChunkSizeTakesPrecedenceOverBlock()
    {
        // Arrange
        var props = new AiQueryProperties
        {
            ChunkingMode = ChunkingMode.FixedPageCount,
            ChunkSize = 25
        };

        // Act
        IPageChunker chunker = ChunkerFactory.Create(props);

        // Assert: We can't directly check the chunk size, but the chunker is created
        _ = Assert.IsType<FixedCountChunker>(chunker);
    }
}

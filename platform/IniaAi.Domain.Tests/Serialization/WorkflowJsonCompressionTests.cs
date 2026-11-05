using IniaAi.Domain.Serialization;

namespace IniaAi.Domain.Tests.Serialization;

public class WorkflowJsonCompressionTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Name\":\"Report\",\"Blocks\":[1,2,3],\"Nested\":{\"a\":true}}")]
    [InlineData("unicode ☃ é 漢字 with \"quotes\" and\nnewlines\tand tabs")]
    [InlineData("")]
    public void RoundTrips(string json)
    {
        byte[] compressed = WorkflowJsonCompression.Compress(json);

        Assert.Equal(json, WorkflowJsonCompression.Decompress(compressed));
    }

    [Fact]
    public void Decompress_EmptyBytes_ReturnsEmptyString()
    {
        Assert.Equal(string.Empty, WorkflowJsonCompression.Decompress([]));
    }

    [Fact]
    public void Compress_LargeRepetitiveJson_ShrinksSubstantially()
    {
        string big = "{\"prompt\":\"" + new string('a', 100_000) + "\"}";

        byte[] compressed = WorkflowJsonCompression.Compress(big);

        Assert.True(
            compressed.Length < big.Length / 5,
            $"expected >5x compression, got {big.Length} -> {compressed.Length} bytes");
    }
}

using IniaAi.Common.Embeddings;

namespace IniaAi.Common.Tests;

public class EmbeddingsTests
{
    [Fact]
    public void AverageVectors_WhenInputListIsEmpty_ThrowsArgumentException()
    {
        // Arrange
        List<double[]> vectors = [];

        // Act & Assert
        _ = Assert.Throws<ArgumentException>(() => EmbeddingsUtils.AverageVectors(vectors));
    }

    [Fact]
    public void AverageVectors_WhenInputVectorsHaveDifferentLength_ThrowsArgumentException()
    {
        // Arrange
        List<double[]> vectors =
            [
                [1.0, 2.0, 3.0],
                [4.0, 5.0],
                [6.0, 7.0, 8.0]
            ];

        // Act & Assert
        _ = Assert.Throws<ArgumentException>(() => EmbeddingsUtils.AverageVectors(vectors));
    }

    [Fact]
    public void AverageVectors_WhenInputVectorsHaveSameLength_ReturnsCorrectAverage()
    {
        // Arrange
        List<double[]> vectors =
        [
            [1.0, 2.0, 3.0],
            [4.0, 5.0, 6.0],
            [7.0, 8.0, 9.0]
        ];

        double[] expectedAverage = [4.0, 5.0, 6.0];

        // Act
        double[] actualAverage = EmbeddingsUtils.AverageVectors(vectors);

        // Assert
        Assert.Equal(expectedAverage, actualAverage);
    }

    [Fact]
    public void GetSimilarityScore_WhenInputArraysHaveSameLength_ReturnsCorrectSimilarityScore()
    {
        // Arrange
        double[] vector1 = [1.0, 2.0, 3.0];
        double[] vector2 = [4.0, 5.0, 6.0];
        double expectedScore = 0.9746318;

        // Act
        double actualScore = EmbeddingsUtils.GetSimilarityScore(vector1, vector2);

        // Assert
        Assert.Equal(expectedScore, actualScore, 0.0001);
    }

    [Fact]
    public void GetSimilarityScore_WhenInputArraysHaveDifferentLength_ThrowsArgumentException()
    {
        // Arrange
        double[] vector1 = [1.0, 2.0, 3.0];
        double[] vector2 = [4.0, 5.0];

        // Act & Assert
        _ = Assert.Throws<ArgumentException>(() => EmbeddingsUtils.GetSimilarityScore(vector1, vector2));
    }

    [Fact]
    public void GetSimilarityScore_WhenInputArraysAreEmpty_ReturnsZero()
    {
        // Arrange
        double[] vector1 = [];
        double[] vector2 = [];
        double expectedScore = 0.0;

        // Act
        double actualScore = EmbeddingsUtils.GetSimilarityScore(vector1, vector2);

        // Assert
        Assert.Equal(expectedScore, actualScore);
    }

    [Fact]
    public void RankVectors_ReturnsSortedScoresInDescendingOrder()
    {
        // Arrange
        var vectors = new Dictionary<int, double[]>
        {
            { 1, [0.1, 0.2, 0.3] },
            { 2, [4.0, 5.0, 6.0] },
            { 3, [10.0, 11.0, 12.0] }
        };

        double[] filter = [5.0, 6.0, 7.0];

        // Act
        List<(double Score, int Index)> actualScores = EmbeddingsUtils.RankVectors(vectors, filter);

        // Assert
        Assert.Equal(3, actualScores.Count);
        Assert.Equal(2, actualScores[0].Index);
        Assert.Equal(3, actualScores[1].Index);
        Assert.Equal(1, actualScores[2].Index);
    }

    [Fact]
    public void GetBestMatchesWithNeighbors_ReturnsExpectedResults()
    {
        // Arrange
        var vectors = new Dictionary<int, double[]>
            {
                { 1, [7.0, 8.0, 9.0] },
                { 2, [100.0, 101.0, 102.0] },
                { 3, [200.0, 201.0, 202.0] },
                { 4, [300.0, 301.0, 302.0] },
                { 5, [400.0, 401.0, 402.0] },
                { 6, [500.0, 501.0, 502.0] },
                { 7, [600.0, 601.0, 602.0] },
                { 8, [700.0, 701.0, 702.0] },
                { 9, [800.0, 801.0, 802.0] },
                { 10, [0.01, 0.02, 0.03] },
                { 11, [1000.0, 1001.0, 1002.0] },
                { 12, [1100.0, 1101.0, 1102.0] },
                { 13, [1200.0, 1201.0, 1202.0] },
                { 14, [1300.0, 1301.0, 1302.0] },
                { 15, [1400.0, 1401.0, 1402.0] },
                { 16, [1500.0, 1501.0, 1502.0] },
                { 17, [1600.0, 1601.0, 1602.0] },
                { 18, [1700.0, 1701.0, 1702.0] },
                { 19, [13.0, 14.0, 15.0] },
                { 20, [1900.0, 1901.0, 1902.0] }
            };

        List<double[]> filters =
        [
            [13.0, 14.0, 15.0],
            [0.01, 0.02, 0.03],
            [7.0, 8.0, 9.0]
        ];

        // Act
        List<int> actualIndices = EmbeddingsUtils.GetBestMatchesWithNeighbors(vectors, filters, 2);

        // Assert
        Assert.Equal(8, actualIndices.Count);
        Assert.Equal(1, actualIndices[0]);
        Assert.Equal(2, actualIndices[1]);
        Assert.Equal(9, actualIndices[2]);
        Assert.Equal(10, actualIndices[3]);
        Assert.Equal(11, actualIndices[4]);
        Assert.Equal(18, actualIndices[5]);
        Assert.Equal(19, actualIndices[6]);
        Assert.Equal(20, actualIndices[7]);
    }

    [Fact]
    public void RemoveDoubledEntiresWithLowerScores_ReturnsUniqueScores()
    {
        // Arrange
        var scores = new List<(double Score, int Index)>
        {
            (0.9, 1),
            (0.8, 2),
            (0.9, 3),
            (0.8, 2),
            (0.9, 6),
            (0.6, 2),
            (0.7, 8),
            (0.8, 10),
            (0.5, 10),
            (0.7, 10),
        };

        // Act
        EmbeddingsUtils.RemoveDoubledEntiresWithLowerScores(scores);

        // Assert
        Assert.Equal(6, scores.Count);
        Assert.Equal(1, scores[0].Index);
        Assert.Equal(2, scores[1].Index);
        Assert.Equal(3, scores[2].Index);
        Assert.Equal(6, scores[3].Index);
        Assert.Equal(8, scores[4].Index);
        Assert.Equal(10, scores[5].Index);
    }
}

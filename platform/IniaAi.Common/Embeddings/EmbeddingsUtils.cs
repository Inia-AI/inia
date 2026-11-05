using System.Numerics;

namespace IniaAi.Common.Embeddings;

/// <summary>
/// Provides methods for working with vector embeddings.
/// </summary>
public class EmbeddingsUtils
{
    /// <summary>
    /// Calculates the average of a list of vectors.
    /// </summary>
    /// <param name="vectors">The list of vectors.</param>
    /// <returns>The average vector.</returns>
    /// <exception cref="ArgumentException">Thrown when the input list is empty or the vectors have different lengths.</exception>
    public static double[] AverageVectors(List<double[]> vectors)
    {
        if (vectors.Count == 0)
        {
            throw new ArgumentException("Input list must not be empty.");
        }

        if (!vectors.All(v => v.Length == vectors[0].Length))
        {
            throw new ArgumentException("Input vectors must have the same length.");
        }

        int vectorSize = Vector<double>.Count;
        int alignedLength = vectors[0].Length - (vectors[0].Length % vectorSize);

        double[] average = new double[vectors[0].Length];

        for (int i = 0; i < alignedLength; i += vectorSize)
        {
            Vector<double> sum = Vector<double>.Zero;

            foreach (double[] vector in vectors)
            {
                sum += new Vector<double>(vector, i);
            }

            sum.CopyTo(average, i);
        }

        for (int i = alignedLength; i < vectors[0].Length; i++)
        {
            foreach (double[] vector in vectors)
            {
                average[i] += vector[i];
            }
        }

        for (int i = 0; i < average.Length; i++)
        {
            average[i] /= vectors.Count;
        }

        return average;
    }

    /// <summary>
    /// Calculates the similarity score between two vectors.
    /// </summary>
    /// <param name="vector1">The first vector.</param>
    /// <param name="vector2">The second vector.</param>
    /// <returns>The similarity score.</returns>
    /// <exception cref="ArgumentException">Thrown when the input arrays have different lengths.</exception>
    public static double GetSimilarityScore(double[] vector1, double[] vector2)
    {
        if (vector1.Length != vector2.Length)
        {
            throw new ArgumentException("Input arrays must have the same length.");
        }

        int vectorSize = Vector<double>.Count;
        int alignedLength = vector1.Length - (vector1.Length % vectorSize);

        Vector<double> dotProductVec = Vector<double>.Zero;
        Vector<double> normAVec = Vector<double>.Zero;
        Vector<double> normBVec = Vector<double>.Zero;

        for (int i = 0; i < alignedLength; i += vectorSize)
        {
            var v1 = new Vector<double>(vector1, i);
            var v2 = new Vector<double>(vector2, i);

            dotProductVec += v1 * v2;
            normAVec += v1 * v1;
            normBVec += v2 * v2;
        }

        double dotProduct = Vector.Dot(dotProductVec, Vector<double>.One);
        double normA = Vector.Dot(normAVec, Vector<double>.One);
        double normB = Vector.Dot(normBVec, Vector<double>.One);

        // Handle any remaining elements
        for (int i = alignedLength; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            normA += vector1[i] * vector1[i];
            normB += vector2[i] * vector2[i];
        }

        return normA == 0 || normB == 0
            ? 0.0
            : dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    /// <summary>
    /// Ranks the vectors based on their similarity scores to a given filter.
    /// </summary>
    /// <param name="vectors">The dictionary of vectors.</param>
    /// <param name="filter">The filter vector to compare against.</param>
    /// <returns>A list of tuples containing the similarity scores and indices of the vectors, sorted in descending order.</returns>
    public static List<(double Score, int Index)> RankVectors(Dictionary<int, double[]> vectors, double[] filter)
    {
        var scores = new List<(double Score, int Index)>();

        foreach ((int index, double[] vector) in vectors)
        {
            double score = GetSimilarityScore(vector, filter);
            scores.Add((score, index));
        }

        scores.Sort((a, b) => b.Score.CompareTo(a.Score)); // Sort in descending order
        return scores;
    }

    /// <summary>
    /// Gets the best matches with neighbors for a list of filters.
    /// </summary>
    /// <param name="vectors">The dictionary of vectors.</param>
    /// <param name="filters">The list of filter vectors.</param>
    /// <param name="numberOfVectorsToTake">The number of best matches to take for each filter.</param>
    /// <returns>A list of indices representing the best matches and their
    /// neighbors. The list is sorted in ascending order.</returns>
    public static List<int> GetBestMatchesWithNeighbors(
        Dictionary<int, double[]> vectors,
        List<double[]> filters,
        int numberOfVectorsToTake)
    {
        if (vectors.Count == 0)
        {
            return [];
        }

        var result = new SortedSet<int>();
        int minIndex = vectors.Keys.Min();
        int maxIndex = vectors.Keys.Max();

        var bestMatches = new List<int>();

        foreach (double[] filter in filters)
        {
            List<(double Score, int Index)> scores = RankVectors(vectors, filter);

            for (int i = 0; i < numberOfVectorsToTake && i < scores.Count; i++)
            {
                bestMatches.Add(scores[i].Index);
            }
        }

        foreach (int bestMatch in bestMatches)
        {
            _ = result.Add(bestMatch);

            if (bestMatch > minIndex)
            {
                _ = result.Add(bestMatch - 1);
            }

            if (bestMatch < maxIndex)
            {
                _ = result.Add(bestMatch + 1);
            }
        }

        return [.. result];
    }

    /// <summary>
    /// Removes entries with lower scores when there are multiple entries with
    /// the same index.
    /// </summary>
    /// <param name="scores"></param>
    public static void RemoveDoubledEntiresWithLowerScores(List<(double Score, int Index)> scores)
    {
        var uniqueScores = new Dictionary<int, double>();

        foreach ((double score, int index) in scores)
        {
            if (!uniqueScores.TryGetValue(index, out double value))
            {
                uniqueScores[index] = score;
            }
            else if (score > value)
            {
                uniqueScores[index] = score;
            }
        }

        scores.Clear();

        foreach ((int index, double score) in uniqueScores)
        {
            scores.Add((score, index));
        }
    }
}

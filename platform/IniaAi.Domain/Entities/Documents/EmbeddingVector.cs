using System.ComponentModel.DataAnnotations;

namespace IniaAi.Domain.Entities.Documents;

public class EmbeddingVector
{
    [Key]
    public string Id { get; private set; } = Guid.NewGuid().ToString();

    public string Model { get; set; } = string.Empty;

    public double[] Vector { get; set; } = [];

    internal EmbeddingVector()
    {
    }

    public EmbeddingVector(string model, double[] vector)
    {
        Model = model;
        Vector = vector;
    }
}

using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Domain.Entities.Workflows.Enums;

/// <summary>The name a person sees for each block type: the editor shows it, and a run's messages name a block type by it.</summary>
public static class BlockTypeLabels
{
    public static string Of(BlockTypes type)
    {
        return BlockTypeCatalog.Find(type)?.Label ?? $"Unavailable block type {(int)type}";
    }
}

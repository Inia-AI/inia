using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;

/// <summary>
/// Defines properties for blocks that produce documents.
/// </summary>
public interface IProducesDocumentBlockProperties
{
    BlockTypes Type { get; set; }

    string? CreatesDocumentClass { get; set; }

    bool RequiresDocumentClass { get; }
}

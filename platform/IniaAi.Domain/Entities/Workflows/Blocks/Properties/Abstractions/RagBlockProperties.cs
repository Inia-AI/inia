namespace IniaAi.Domain.Entities.Workflows.Blocks.Properties.Abstractions;

/// <summary>
/// Base class for blocks that use RAG (Retrieval-Augmented Generation) for document processing.
/// Note: RagSettings and SupportedDocumentClasses are stored on the Block entity itself as EF relationships/properties.
/// This base class serves as a marker for RAG-capable blocks and can be extended with RAG-specific
/// configuration properties in the future (e.g., caching settings, embedding model overrides).
/// </summary>
public abstract class RagBlockProperties : BlockProperties
{
    // Intentionally empty - RagSettings and SupportedDocumentClasses live on Block entity
    // Future RAG-specific properties can be added here
}

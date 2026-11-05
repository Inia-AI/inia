using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;
using System.Runtime.CompilerServices;

namespace IniaAi.Testing.DependencyInjection;

/// <summary>
/// A block saved outside any section, as a template's block is. Every public constructor places
/// the block in a section, so this skips the constructor, and with it every field initializer:
/// each list the database maps is set here.
/// </summary>
public static class TestBlocks
{
    public static Block AiQuery(
        string id,
        string name,
        string replacementTag,
        DateTime createdAt,
        AiQueryProperties properties,
        string? description = null)
    {
        properties.Type = BlockTypes.AiQuery;

        var block = (Block)RuntimeHelpers.GetUninitializedObject(typeof(Block));
        block.Id = id;
        block.Type = BlockTypes.AiQuery;
        block.Name = name;
        block.Description = description;
        block.Order = 0;
        block.ReplacementTag = replacementTag;
        block.CreatedAt = createdAt;
        block.IsDeleted = false;
        block.Properties = properties;
        block.SupportedDocumentClasses = [];
        return block;
    }
}

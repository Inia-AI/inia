using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;

namespace IniaAi.WebApp.Services;

/// <summary>
/// Centralized service for prompt preview logic to ensure consistency across the application.
/// </summary>
public static class PromptPreviewService
{
    /// <summary>
    /// Determines if a block needs its prompt preview to be generated or regenerated.
    /// </summary>
    /// <param name="block">The workflow block to check</param>
    /// <returns>True if the block needs preview generation, false otherwise</returns>
    public static bool NeedsPreviewGeneration(Block block)
    {
        // Only blocks with user prompt templates need previews
        if (block.Properties is not AiQueryProperties props
            || string.IsNullOrWhiteSpace(props.UserPromptTemplate))
        {
            return false;
        }

        // Only return true if the preview is missing
        // The preview is automatically cleared when the user changes the prompt template
        // (see AiQueryBlock.razor OnUserPromptTemplateChanged method)
        return string.IsNullOrWhiteSpace(props.UserPromptTemplatePreview);
    }

    /// <summary>
    /// Gets all blocks from a workflow that need preview generation.
    /// </summary>
    /// <param name="workflow">The workflow to check</param>
    /// <returns>List of blocks that need preview generation</returns>
    public static List<Block> GetBlocksNeedingPreviews(Domain.Entities.Workflows.Workflow workflow)
    {
        return [.. workflow.GetAllBlocks().Where(NeedsPreviewGeneration)];
    }
}

using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;
using IniaAi.Domain.Entities.Workflows.Blocks.Properties;
using IniaAi.Domain.Entities.Workflows.Enums;

namespace IniaAi.WebApp.Services;

public static class AiQueryCitationDefault
{
    public static void ApplyToNewProperties(Block block)
    {
        if (block.RagSettings.Count > 0
            && SelectsDocuments(block.SupportedDocumentClasses)
            && block.Properties is AiQueryProperties properties)
        {
            properties.ShouldGetCitationsFromModel = true;
        }
    }

    public static void ApplyToAddedFilterRule(Block block)
    {
        if (block.RagSettings.Count == 1)
        {
            ApplyToNewProperties(block);
        }
    }

    public static void ApplyToClassChange(Block block, IReadOnlyCollection<string> previousClasses)
    {
        if (!SelectsDocuments(previousClasses))
        {
            ApplyToNewProperties(block);
        }
    }

    public static void ApplyToContentEdit(Block block, Action edit)
    {
        bool readPagesThroughReaders = ReadsPagesThroughReaders(block);
        edit();

        if (!readPagesThroughReaders
            && ReadsPagesThroughReaders(block)
            && block.Properties is AiQueryProperties properties)
        {
            properties.ShouldGetCitationsFromModel = true;
        }
    }

    private static bool SelectsDocuments(IReadOnlyCollection<string> documentClasses)
    {
        return documentClasses.Count > 0 && !documentClasses.Contains(DocumentClass.KEY_NONE);
    }

    /// <summary>
    /// Whether the block reads document pages first-hand through its inputs: at least one input
    /// is a document reader of page text, and readers are no fewer than its AI inputs. A block
    /// that mostly rewrites other AI output cites through the blocks it reads instead.
    /// </summary>
    private static bool ReadsPagesThroughReaders(Block block)
    {
        if (block.Section?.Workflow is not Workflow workflow)
        {
            return false;
        }

        HashSet<string> tags = [.. block.GetContentDependencyTags()];
        List<Block> inputs = [.. workflow.GetAllBlocks()
            .Where(input => input != block && tags.Contains(input.GetDependencyKey()))];
        int readers = inputs.Count(input => input.Properties is RetrieveDataFromDocumentsProperties { ShouldUseOverviews: false });

        return readers > 0 && readers >= inputs.Count(input => input.Type is BlockTypes.AiQuery or BlockTypes.Agent);
    }
}

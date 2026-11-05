using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Domain.Entities.Workflows;

/// <summary>
/// Where each document class gets its page overview prompt, and which blocks read overviews that a
/// class never gets. A page gets an overview only from its class's prompt or the workflow's
/// default, so a block that searches overviews finds nothing for a class with neither. Coverage
/// is judged by the prompt for pages read with their neighbours: a one-page version alone covers
/// only spreadsheets and CSV files.
/// </summary>
public static class PageOverviewCoverage
{
    /// <param name="Classes">The classes the blocks read that get no overviews; empty when the workflow has no classes and no default.</param>
    public sealed record Gap(IReadOnlyList<Block> Blocks, IReadOnlyList<DocumentClass> Classes)
    {
        /// <summary>What the editor's Document Classes tab and a template save's warning say.</summary>
        public string Message
        {
            get
            {
                string blocks = Blocks.Count == 1 ? "1 block reads" : $"{Blocks.Count} blocks read";
                if (Classes.Count == 0)
                {
                    return $"{blocks} page overviews, but documents get none. Set a default overview prompt.";
                }

                string classes = string.Join(", ", Classes.Select(c => string.IsNullOrWhiteSpace(c.DisplayName) ? c.Key : c.DisplayName));

                return $"{blocks} page overviews, but {classes} documents get none. Give each an overview prompt, or set a default.";
            }
        }
    }

    public static string SourceLabel(DocumentClass documentClass, PageOverviewPrompts? workflowDefault)
    {
        if (documentClass.PageOverviews is { IsEmpty: false })
        {
            return "Own";
        }

        return workflowDefault is { IsEmpty: false } ? "Workflow default" : "None";
    }

    public static Gap? FindGap(Workflow workflow)
    {
        PageOverviewPrompts? workflowDefault = workflow.Configuration?.PageOverviews;
        List<DocumentClass> inputClasses = [.. workflow.DocumentClasses.Where(c => !c.IsOutputDocument)];
        List<Block> readers = [.. workflow.GetAllBlocks().Where(block => block.ReadsPageOverviews())];
        if (readers.Count == 0)
        {
            return null;
        }

        if (inputClasses.Count == 0)
        {
            return PageOverviewPrompts.Resolve(null, workflowDefault, singlePage: false) is null ? new Gap(readers, []) : null;
        }

        HashSet<DocumentClass> uncovered = [.. inputClasses.Where(c => PageOverviewPrompts.Resolve(c.PageOverviews, workflowDefault, singlePage: false) is null)];
        List<Block> blocks = [];
        HashSet<DocumentClass> classes = [];
        foreach (Block reader in readers)
        {
            List<DocumentClass> missed = [.. ClassesRead(reader, inputClasses).Where(uncovered.Contains)];
            if (missed.Count > 0)
            {
                blocks.Add(reader);
                classes.UnionWith(missed);
            }
        }

        return blocks.Count == 0
            ? null
            : new Gap(blocks, [.. inputClasses.Where(classes.Contains)]);
    }

    private static IEnumerable<DocumentClass> ClassesRead(Block block, List<DocumentClass> inputClasses)
    {
        List<string> keys = block.SupportedDocumentClasses;
        if (keys.Count == 0 || keys.Contains(DocumentClass.KEY_ANY))
        {
            return inputClasses;
        }

        return inputClasses.Where(c => keys.Contains(c.Key));
    }
}

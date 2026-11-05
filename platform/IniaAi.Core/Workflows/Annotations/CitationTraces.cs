using IniaAi.Domain.Entities.Citations.Traces;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Workflows.Annotations;

/// <summary>
/// Puts a block's citation trace on the block, so the block's next persist writes it beside the
/// block. The trace is written once the block's annotations are done, and again when its report
/// paragraph adds its own decisions.
/// </summary>
internal static class CitationTraces
{
    public static void Record(
        Block block,
        BlockAnnotations annotations,
        PageIndex pages,
        WorkflowAnnotationsContext context,
        string? organizationId)
    {
        Attach(block, annotations.Trace.Finish(annotations, pages, context.FactOrigins, block.Id), organizationId);
    }

    /// <summary>Adds the report paragraph's decisions, when the block built one.</summary>
    public static void RecordReport(Block block, ReportParagraph? paragraph, BlockAnnotations? annotations, string? organizationId)
    {
        if (paragraph is not null && annotations?.Trace.FinishReport() is CitationTraceContent content)
        {
            Attach(block, content, organizationId);
        }
    }

    private static void Attach(Block block, CitationTraceContent content, string? organizationId)
    {
        block.CitationTrace ??= new BlockCitationTrace { BlockId = block.Id };
        block.CitationTrace.OrganizationId = organizationId ?? block.CitationTrace.OrganizationId;
        block.CitationTrace.Content = content.ToJson();
        block.CitationTrace.RecordedAt = DateTime.UtcNow;
    }
}

using ErrorOr;
using IniaAi.Core.Workflows.Annotations;
using IniaAi.Domain.Entities.WorkflowExecutions;
using IniaAi.Domain.Entities.Workflows;
using IniaAi.Domain.Entities.Workflows.Blocks;

namespace IniaAi.Core.Workflows.Outputs.Interfaces;

public interface IWorkflowOutputBuilder
{
    Task<ReportSection?> BuildSectionAsync(Section section, WorkflowOutputContext? context);

    Task<ReportParagraph?> BuildParagraphAsync(
        Block block,
        WorkflowOutputContext? context,
        BlockAnnotations? blockAnnotations,
        WorkflowAnnotationsContext? annotationsContext = null);

    ErrorOr<ReportDataOutput> BuildDataOutput(
        Block block,
        WorkflowOutputContext? context,
        BlockAnnotations? blockAnnotations,
        WorkflowAnnotationsContext? annotationsContext = null);
}

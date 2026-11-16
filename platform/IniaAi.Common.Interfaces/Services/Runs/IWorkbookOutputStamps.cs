using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Common.Interfaces.Services.Runs;

/// <summary>
/// Whether the steps of a workflow that make an output class stamp the workbook they make: the mark that tells that
/// workflow's file from any other workbook. The platform makes no workbook, so it knows none; a closed module whose
/// steps make workbooks answers.
/// </summary>
public interface IWorkbookOutputStamps
{
    /// <summary>Whether every step that makes the class stamps it; null when no step of the workflow makes it as a workbook.</summary>
    bool? Stamps(Workflow maker, string outputClassKey);
}

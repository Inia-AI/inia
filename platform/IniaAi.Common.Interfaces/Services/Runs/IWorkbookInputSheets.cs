using IniaAi.Domain.Entities.Workflows;

namespace IniaAi.Common.Interfaces.Services.Runs;

/// <summary>
/// The sheets a workflow reads by name from the workbook of one of its input classes, so a run can be refused before
/// it starts when its workbook lacks one. The platform reads no sheet by name, so it names none; a closed module whose
/// steps read sheets names them.
/// </summary>
public interface IWorkbookInputSheets
{
    /// <summary>The sheet names, compared ignoring case; empty when no step of the workflow reads the class by sheet.</summary>
    IReadOnlySet<string> SheetsRead(Workflow workflow, string documentClassKey);
}
